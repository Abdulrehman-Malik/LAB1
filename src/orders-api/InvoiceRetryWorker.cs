using System.Diagnostics;
using Grpc.Core;
using Lab.Contracts.Billing;
using Microsoft.EntityFrameworkCore;
using OrdersApi.Data;

namespace OrdersApi;

/// <summary>
/// Eventual consistency: if billing-api was down or too slow when an order was placed, the order is saved
/// with status "Created" and no invoice. Every 30 s this job asks billing-api again for such orders.
/// Retrying is safe because CreateInvoice is idempotent (billing-api returns the existing invoice for an order).
/// Runs in every orders-api replica; two replicas retrying the same order is harmless for the same reason.
/// </summary>
public class InvoiceRetryWorker(IServiceScopeFactory scopes, ILogger<InvoiceRetryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            // Each run gets its own trace id, so its log lines and gRPC calls can be found with scripts/trace.sh
            using (new Activity("InvoiceRetryRun").Start())
            {
                try
                {
                    await RetryAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Invoice retry run failed: {Message}", ex.Message);
                }
            }
        }
    }

    private async Task RetryAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var billing = scope.ServiceProvider.GetRequiredService<BillingService.BillingServiceClient>();

        // Skip very recent orders: their original request may still be talking to billing-api
        var cutoff = DateTime.UtcNow - Interval;
        var waiting = await db.Orders
            .Where(o => o.Status == OrderStatus.Created && o.CreatedAt < cutoff)
            .OrderBy(o => o.Id)
            .Take(20)
            .ToListAsync(ct);

        foreach (var order in waiting)
        {
            try
            {
                var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
                {
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    AmountCents = (long)(order.Amount * 100),
                }, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: ct);

                order.Status = OrderStatus.Invoiced;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Retry: order {OrderId} is now invoiced (invoice {InvoiceId})", order.Id, invoice.Id);
            }
            catch (RpcException ex)
            {
                logger.LogWarning("Retry: order {OrderId} still not invoiced, billing-api: {Status}. Next try in {Seconds} s",
                    order.Id, ex.StatusCode, Interval.TotalSeconds);
                break;   // billing-api is still unhappy; don't hammer it with the rest of the list
            }
        }
    }
}
