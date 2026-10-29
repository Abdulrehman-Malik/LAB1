using Grpc.Core;
using Lab.Contracts.Billing;
using Lab.Contracts.Customers;
using Lab.Shared;
using Microsoft.EntityFrameworkCore;
using OrdersApi;
using OrdersApi.Data;

var builder = WebApplication.CreateBuilder(args);

// Secrets from /run/secrets, REST on 8080 (see Lab.Shared). orders-api has no gRPC server of its own.
builder.AddLabDefaults();

builder.Services.AddLabDbContext<OrdersDbContext>(builder.Configuration, "SalesDb", OrdersDbContext.Schema);
builder.Services.AddHealthChecks().AddDbContextCheck<OrdersDbContext>();

// gRPC clients. Addresses are http:// (HTTP/2 without TLS), e.g. http://customers-api:8081 inside labnet
builder.Services.AddGrpcClient<CustomerService.CustomerServiceClient>(o => o.Address = new Uri(builder.Configuration["Services:Customers"]!));
builder.Services.AddGrpcClient<BillingService.BillingServiceClient>(o => o.Address = new Uri(builder.Configuration["Services:Billing"]!));

builder.Services.AddStackExchangeRedisCache(o =>
{
    o.Configuration = builder.Configuration["Redis:Connection"];
    o.InstanceName = "orders-api:";   // key prefix, so services sharing Redis don't collide
});
builder.Services.AddScoped<OrderDetailsService>();
builder.Services.AddHostedService<InvoiceRetryWorker>();   // retries invoices for orders stuck in "Created"

var app = builder.Build();
app.UseLabDefaults();   // X-Served-By / X-Trace-Id headers, request log, graceful shutdown

await app.MigrateDatabaseAsync<OrdersDbContext>();

var api = app.MapGroup("/api/orders");

api.MapHealthChecks("/health");

api.MapGet("", async (OrdersDbContext db) =>
    await db.Orders.AsNoTracking().OrderByDescending(o => o.Id).ToListAsync());

// Order + customer (gRPC) + invoice (gRPC), cached in Redis. The X-Cache header shows HIT or MISS.
api.MapGet("/{id:int}", async (int id, OrderDetailsService details, HttpContext http) =>
{
    var (result, fromCache) = await details.GetAsync(id, http.RequestAborted);
    if (result is null)
        return Results.NotFound();

    http.Response.Headers["X-Cache"] = fromCache ? "HIT" : "MISS";
    return Results.Ok(result);
});

api.MapPost("", async (CreateOrderRequest request, OrdersDbContext db,
    CustomerService.CustomerServiceClient customers, BillingService.BillingServiceClient billing, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Product) || request.Quantity <= 0 || request.UnitPrice <= 0)
        return Results.BadRequest("Product, a positive Quantity and a positive UnitPrice are required.");

    // gRPC call 1: the customer must exist before we save anything
    try
    {
        await customers.GetCustomerAsync(new GetCustomerRequest { Id = request.CustomerId }, deadline: DateTime.UtcNow.AddSeconds(5));
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
    {
        return Results.BadRequest($"Customer {request.CustomerId} does not exist.");
    }
    catch (RpcException ex)
    {
        return Results.Problem($"customers-api is unavailable: {ex.Status.Detail}", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var order = new Order
    {
        CustomerId = request.CustomerId,
        Product = request.Product,
        Quantity = request.Quantity,
        UnitPrice = request.UnitPrice,
        Amount = request.Quantity * request.UnitPrice,
    };
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    logger.LogInformation("Order {OrderId} saved: customer {CustomerId}, {Product} x{Quantity} = {Amount}",
        order.Id, order.CustomerId, order.Product, order.Quantity, order.Amount);

    // gRPC call 2: billing-api creates the invoice. If billing is down, the order is kept with Status "Created".
    try
    {
        var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            AmountCents = (long)(order.Amount * 100),
        }, deadline: DateTime.UtcNow.AddSeconds(5));

        order.Status = OrderStatus.Invoiced;
        await db.SaveChangesAsync();
        logger.LogInformation("Order {OrderId} invoiced: billing-api created invoice {InvoiceId}", order.Id, invoice.Id);
    }
    catch (RpcException ex)
    {
        logger.LogWarning("Order {OrderId} saved, but billing-api failed: {Status}", order.Id, ex.Status);
    }

    return Results.Created($"/api/orders/{order.Id}", order);
});

app.Run();

record CreateOrderRequest(int CustomerId, string Product, int Quantity, decimal UnitPrice);
