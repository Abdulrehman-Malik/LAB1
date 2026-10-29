using System.Text.Json.Nodes;

namespace Autoscaler;

/// <summary>
/// Every checkIntervalSeconds: for each service in autoscale.yml, measure average CPU across its replicas
/// (as a % of the service's CPU limit) and add or remove one replica, staying between min and max.
/// </summary>
public class AutoscalerWorker(DockerClient docker, IConfiguration configuration, ILogger<AutoscalerWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, DateTime> lastChange = [];
    private readonly Dictionary<string, int> checksAbove = [];   // consecutive checks with CPU above the scale-up threshold
    private readonly Dictionary<string, int> checksBelow = [];   // consecutive checks with CPU below the scale-down threshold

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var path = configuration["Autoscale:ConfigFile"] ?? "/config/autoscale.yml";
        logger.LogInformation("Autoscaler started, rules from {Path}", path);

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = AutoscaleConfig.Load(path);
            foreach (var (name, rule) in config.Services)
            {
                try
                {
                    await CheckServiceAsync($"{config.Stack}_{name}", rule, config.StartupGraceSeconds, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("{Service}: check failed: {Message}", name, ex.Message);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(config.CheckIntervalSeconds), stoppingToken);
        }
    }

    private async Task CheckServiceAsync(string serviceName, ServiceRule rule, int startupGraceSeconds, CancellationToken ct)
    {
        var service = await docker.GetServiceAsync(serviceName, ct);
        if (service?["Spec"]?["Mode"]?["Replicated"] is null)
        {
            logger.LogDebug("{Service}: not found or not replicated, skipping", serviceName);
            return;
        }

        var replicas = (int)(long)service["Spec"]!["Mode"]!["Replicated"]!["Replicas"]!;
        var cpuPercent = await MeasureCpuPercentAsync(serviceName, service, startupGraceSeconds, ct);

        var inCooldown = lastChange.TryGetValue(serviceName, out var changedAt)
            && DateTime.UtcNow - changedAt < TimeSpan.FromSeconds(rule.CooldownSeconds);
        checksAbove[serviceName] = cpuPercent > rule.ScaleUpAbovePercent ? checksAbove.GetValueOrDefault(serviceName) + 1 : 0;
        checksBelow[serviceName] = cpuPercent < rule.ScaleDownBelowPercent ? checksBelow.GetValueOrDefault(serviceName) + 1 : 0;
        var (target, reason) = ScalingDecision.Decide(rule, replicas, cpuPercent, inCooldown,
            checksAbove[serviceName], checksBelow[serviceName]);
        logger.LogInformation("{Service}: {Replicas} replica(s), CPU {Cpu:F0}% of limit -> {Decision}",
            serviceName, replicas, cpuPercent, target == replicas ? reason : $"scale to {target} ({reason})");

        if (target != replicas)
        {
            await docker.ScaleAsync(service, target, ct);
            lastChange[serviceName] = DateTime.UtcNow;
            checksAbove[serviceName] = checksBelow[serviceName] = 0;   // start counting again after a change
        }
    }

    /// <summary>
    /// Average CPU of the running replicas, as a % of the per-replica CPU limit (deploy.resources.limits.cpus).
    /// Replicas younger than the startup grace period are ignored: .NET burns CPU while starting (JIT, migrations),
    /// which would otherwise look like load and trigger a scale-up nobody needs.
    /// </summary>
    private async Task<double> MeasureCpuPercentAsync(string serviceName, JsonNode service, int startupGraceSeconds, CancellationToken ct)
    {
        var nanoCpus = (long?)service["Spec"]?["TaskTemplate"]?["Resources"]?["Limits"]?["NanoCPUs"] ?? 0;
        var limitCores = nanoCpus > 0 ? nanoCpus / 1e9 : 1.0;   // no limit set: measure against one full CPU

        var settled = (await docker.GetRunningContainersAsync(serviceName, ct))
            .Where(c => DateTime.UtcNow - c.RunningSince.ToUniversalTime() > TimeSpan.FromSeconds(startupGraceSeconds))
            .Select(c => c.ContainerId);
        var samples = (await Task.WhenAll(settled.Select(id => docker.GetCpuCoresAsync(id, ct)))).OfType<double>().ToList();
        return samples.Count == 0 ? 0 : samples.Average() / limitCores * 100;
    }
}
