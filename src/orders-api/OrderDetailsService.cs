using System.Text.Json;
using Grpc.Core;
using Lab.Contracts.Billing;
using Lab.Contracts.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using OrdersApi.Data;

namespace OrdersApi;

public record OrderDetails(Order Order, CustomerInfo? Customer, InvoiceInfo? Invoice);
public record CustomerInfo(int Id, string Name, string Email, string City);
public record InvoiceInfo(int Id, decimal Amount, string Status, bool EmailSent);

/// <summary>
/// Builds the "order details" view: the order from SQL, the customer from customers-api (gRPC)
/// and the invoice from billing-api (gRPC), cached in Redis (cache-aside pattern).
/// </summary>
public class OrderDetailsService(
    OrdersDbContext db,
    CustomerService.CustomerServiceClient customers,
    BillingService.BillingServiceClient billing,
    IDistributedCache cache,
    ILogger<OrderDetailsService> logger)
{
    private static readonly DistributedCacheEntryOptions CacheFor = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };
    private static readonly TimeSpan GrpcTimeout = TimeSpan.FromSeconds(5);

    public async Task<(OrderDetails? Details, bool FromCache)> GetAsync(int id, CancellationToken ct)
    {
        var key = $"order:{id}";

        // 1. Ask Redis first (cache hit: about 1 ms, no SQL and no gRPC)
        if (await TryGetCachedAsync(key, ct) is { } cached)
        {
            logger.LogDebug("Cache HIT for {Key}", key);
            return (JsonSerializer.Deserialize<OrderDetails>(cached), true);
        }

        // 2. Cache miss: read SQL, then call the two other services
        logger.LogDebug("Cache MISS for {Key}: reading SQL and calling customers-api + billing-api", key);
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            return (null, false);

        var details = new OrderDetails(order, await GetCustomerAsync(order.CustomerId, ct), await GetInvoiceAsync(order.Id, ct));

        // 3. Save in Redis for next time, but only complete answers: don't cache a gap caused by a service being down
        if (details.Customer is not null && details.Invoice is not null)
            await TrySetCachedAsync(key, JsonSerializer.Serialize(details), ct);

        return (details, false);
    }

    private async Task<CustomerInfo?> GetCustomerAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var c = await customers.GetCustomerAsync(new GetCustomerRequest { Id = customerId },
                deadline: DateTime.UtcNow.Add(GrpcTimeout), cancellationToken: ct);
            return new CustomerInfo(c.Id, c.Name, c.Email, c.City);
        }
        catch (RpcException ex)
        {
            logger.LogWarning("customers-api call for customer {CustomerId} failed: {Status}", customerId, ex.Status);
            return null;
        }
    }

    private async Task<InvoiceInfo?> GetInvoiceAsync(int orderId, CancellationToken ct)
    {
        try
        {
            var i = await billing.GetInvoiceByOrderAsync(new GetInvoiceByOrderRequest { OrderId = orderId },
                deadline: DateTime.UtcNow.Add(GrpcTimeout), cancellationToken: ct);
            return new InvoiceInfo(i.Id, i.AmountCents / 100m, i.Status, i.EmailSent);
        }
        catch (RpcException ex)
        {
            logger.LogWarning("billing-api call for order {OrderId} failed: {Status}", orderId, ex.Status);
            return null;
        }
    }

    // Redis is a cache, not the source of truth: if it is down, log it and carry on without it
    private async Task<string?> TryGetCachedAsync(string key, CancellationToken ct)
    {
        try { return await cache.GetStringAsync(key, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Redis read failed, using the database: {Message}", ex.Message);
            return null;
        }
    }

    private async Task TrySetCachedAsync(string key, string value, CancellationToken ct)
    {
        try { await cache.SetStringAsync(key, value, CacheFor, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Redis write failed: {Message}", ex.Message);
        }
    }
}
