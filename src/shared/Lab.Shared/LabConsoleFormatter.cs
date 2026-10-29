using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Lab.Shared;

/// <summary>
/// One log line per entry, the same in every service:
/// <code>09:41:07.123 INFO  trace=4bf92f3577b34da6a3ce929d0e0e4736 OrderDetailsService: Cache MISS for order 7</code>
/// The trace id is the same in every service that worked on one request (ASP.NET Core and gRPC pass it along
/// in the "traceparent" header), so <c>bash scripts/trace.sh &lt;trace-id&gt;</c> finds the whole story.
/// </summary>
public sealed class LabConsoleFormatter() : ConsoleFormatter(Name)
{
    public new const string Name = "lab";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null)
            return;

        var trace = Activity.Current?.TraceId.ToString() ?? "-";
        textWriter.Write($"{DateTime.UtcNow:HH:mm:ss.fff} {Level(logEntry.LogLevel)} trace={trace} {ShortCategory(logEntry.Category)}: {message}");
        if (logEntry.Exception is not null)
            textWriter.Write($"{Environment.NewLine}{logEntry.Exception}");
        textWriter.WriteLine();
    }

    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT ",
        _ => "     ",
    };

    // "OrdersApi.OrderDetailsService" -> "OrderDetailsService"
    private static string ShortCategory(string category) => category[(category.LastIndexOf('.') + 1)..];
}
