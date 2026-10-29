using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Lab.Shared;

public static class LabHostExtensions
{
    /// <summary>
    /// Setup shared by every lab service (call before Build):
    /// <list type="bullet">
    /// <item>Swarm secrets in /run/secrets become settings (a file named Database__Password becomes Database:Password)</item>
    /// <item>REST on Ports:Rest (HTTP/1.1, default 8080), reached through Traefik</item>
    /// <item>gRPC on Ports:Grpc (HTTP/2 without TLS, default 8081), only inside labnet, when <paramref name="grpc"/> is true</item>
    /// <item>The lab log format with trace ids (see <see cref="LabConsoleFormatter"/>)</item>
    /// </list>
    /// Containers use the default ports; appsettings.Development.json gives each service its own ports for local runs.
    /// </summary>
    public static WebApplicationBuilder AddLabDefaults(this WebApplicationBuilder builder, bool grpc = false)
    {
        builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

        var restPort = builder.Configuration.GetValue("Ports:Rest", 8080);
        var grpcPort = builder.Configuration.GetValue("Ports:Grpc", 8081);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(restPort, o => o.Protocols = HttpProtocols.Http1);
            if (grpc)
                kestrel.ListenAnyIP(grpcPort, o => o.Protocols = HttpProtocols.Http2);
        });

        builder.Logging.AddConsole(o => o.FormatterName = LabConsoleFormatter.Name);
        builder.Logging.AddConsoleFormatter<LabConsoleFormatter, ConsoleFormatterOptions>();
        return builder;
    }

    /// <summary>
    /// Middleware shared by every lab service (call right after Build):
    /// <list type="bullet">
    /// <item>Response headers X-Served-By (which replica, e.g. orders-api-2) and X-Trace-Id, shown in the portal's request inspector</item>
    /// <item>One log line per request (REST and gRPC): method, path, status, duration</item>
    /// <item>Graceful shutdown drain (see <see cref="UseLabGracefulShutdown"/>)</item>
    /// </list>
    /// </summary>
    public static WebApplication UseLabDefaults(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Request");

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Served-By"] = Environment.MachineName;
            context.Response.Headers["X-Trace-Id"] = Activity.Current?.TraceId.ToString();

            var started = Stopwatch.GetTimestamp();
            await next(context);

            // Health checks run every few seconds; logging them would bury everything else
            if (!context.Request.Path.Value!.EndsWith("/health", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("{Method} {Path}{Query} -> {Status} in {Elapsed:F0} ms",
                    context.Request.Method, context.Request.Path, context.Request.QueryString, context.Response.StatusCode,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        });

        return app.UseLabGracefulShutdown();
    }

    /// <summary>
    /// When Swarm stops a replica (scale-down, rolling update), keep serving for Shutdown:DrainSeconds before
    /// Kestrel closes its port. Traefik needs a few seconds to notice the replica is going away; without this
    /// delay, requests it still sends here fail with 502 Bad Gateway. 8 s in containers, 0 for local runs
    /// (Development), so Ctrl+C is instant. Override with the setting Shutdown:DrainSeconds (env Shutdown__DrainSeconds).
    /// </summary>
    public static WebApplication UseLabGracefulShutdown(this WebApplication app)
    {
        var drainSeconds = app.Configuration.GetValue("Shutdown:DrainSeconds", app.Environment.IsDevelopment() ? 0 : 8);
        if (drainSeconds > 0)
        {
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Shutdown");
            app.Lifetime.ApplicationStopping.Register(() =>
            {
                logger.LogInformation("Stop requested: draining for {Seconds} s so Traefik can stop routing here", drainSeconds);
                Thread.Sleep(TimeSpan.FromSeconds(drainSeconds));
            });
        }
        return app;
    }
}
