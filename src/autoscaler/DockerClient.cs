using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Autoscaler;

/// <summary>
/// Minimal client for the Docker Engine REST API over the Unix socket /var/run/docker.sock.
/// The docker CLI and Portainer use this same API; e.g. "docker service scale" is GET + POST /services/{id}/update.
/// </summary>
public class DockerClient
{
    private readonly HttpClient http;

    public DockerClient(string socketPath)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };
        // The host name is ignored: every request goes down the socket
        http = new HttpClient(handler) { BaseAddress = new Uri("http://docker/") };
    }

    /// <summary>GET /services/{name}: the service's spec and version, or null if it doesn't exist.</summary>
    public async Task<JsonNode?> GetServiceAsync(string name, CancellationToken ct)
    {
        using var response = await http.GetAsync($"services/{name}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonNode>(ct);
    }

    /// <summary>The service's tasks that are running right now: container ID and when it entered "running".</summary>
    public async Task<List<(string ContainerId, DateTime RunningSince)>> GetRunningContainersAsync(string service, CancellationToken ct)
    {
        var filters = Uri.EscapeDataString($$"""{"service":["{{service}}"],"desired-state":["running"]}""");
        var tasks = await http.GetFromJsonAsync<JsonArray>($"tasks?filters={filters}", ct) ?? [];
        return tasks
            .Where(t => (string?)t?["Status"]?["State"] == "running" && t?["Status"]?["ContainerStatus"]?["ContainerID"] is not null)
            .Select(t => ((string)t!["Status"]!["ContainerStatus"]!["ContainerID"]!, (DateTime)t["Status"]!["Timestamp"]!))
            .ToList();
    }

    /// <summary>
    /// CPU used by one container, in cores (1.0 = one full CPU): two snapshots 2 s apart,
    /// CPU = container CPU time used / total host CPU time in between × number of CPUs.
    /// (A longer window smooths out short bursts; the counters themselves are only refreshed about once a second.)
    /// Returns null if the container is gone or on another node.
    /// </summary>
    public async Task<double?> GetCpuCoresAsync(string containerId, CancellationToken ct)
    {
        var first = await ReadCpuCountersAsync(containerId, ct);
        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        var second = await ReadCpuCountersAsync(containerId, ct);
        if (first is null || second is null)
            return null;

        var containerDelta = second.Value.Container - first.Value.Container;
        var systemDelta = second.Value.System - first.Value.System;
        return systemDelta > 0 ? containerDelta / systemDelta * second.Value.OnlineCpus : 0;
    }

    /// <summary>One snapshot of the cumulative CPU counters (nanoseconds) for a container and for the whole host.</summary>
    private async Task<(double Container, double System, double OnlineCpus)?> ReadCpuCountersAsync(string containerId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"containers/{containerId}/stats?stream=false&one-shot=true", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var cpu = (await response.Content.ReadFromJsonAsync<JsonNode>(ct))?["cpu_stats"];
        if (cpu?["system_cpu_usage"] is null)
            return null;
        return ((double)cpu["cpu_usage"]!["total_usage"]!, (double)cpu["system_cpu_usage"]!, (double?)cpu["online_cpus"] ?? 1);
    }

    /// <summary>Same as "docker service scale name=replicas": send the spec back with a new replica count.</summary>
    public async Task ScaleAsync(JsonNode service, int replicas, CancellationToken ct)
    {
        var id = (string)service["ID"]!;
        var version = (long)service["Version"]!["Index"]!;   // optimistic concurrency: fails if someone else changed it meanwhile
        var spec = service["Spec"]!.DeepClone();
        spec["Mode"]!["Replicated"]!["Replicas"] = replicas;

        using var response = await http.PostAsJsonAsync($"services/{id}/update?version={version}", spec, ct);
        response.EnsureSuccessStatusCode();
    }
}
