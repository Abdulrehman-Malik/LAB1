using Lab.Contracts.Customers;
using Lab.Shared;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NotificationWorker;

var builder = WebApplication.CreateBuilder(args);

// Secrets from /run/secrets (Database__Password, Mongo__Password), REST on 8080, lab log format
builder.AddLabDefaults();

builder.Services.AddGrpcClient<CustomerService.CustomerServiceClient>(o => o.Address = new Uri(builder.Configuration["Services:Customers"]!));
builder.Services.AddSingleton<AuditLog>();
builder.Services.AddHostedService<InvoiceEmailWorker>();   // the background job
builder.Services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb");

var app = builder.Build();
app.UseLabDefaults();   // X-Served-By / X-Trace-Id headers, request log, graceful shutdown

var api = app.MapGroup("/api/notifications");

api.MapHealthChecks("/health");

// The last 50 audit events from MongoDB (shown on the portal's Activity page)
api.MapGet("/audit", async (AuditLog audit, CancellationToken ct) => await audit.RecentAsync(50, ct));

app.Run();

class MongoHealthCheck(AuditLog audit) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await audit.PingAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            // Message only, no exception: the driver's stack traces are 30+ lines and bury the useful log lines
            return HealthCheckResult.Unhealthy($"MongoDB unreachable: {ex.GetType().Name}");
        }
    }
}
