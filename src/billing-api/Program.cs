using BillingApi.Data;
using BillingApi.Grpc;
using Lab.Shared;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Secrets from /run/secrets, REST on 8080, gRPC on 8081 (see Lab.Shared)
builder.AddLabDefaults(grpc: true);

builder.Services.AddLabDbContext<BillingDbContext>(builder.Configuration, "SalesDb", BillingDbContext.Schema);
builder.Services.AddGrpc();
builder.Services.AddHealthChecks().AddDbContextCheck<BillingDbContext>();

var app = builder.Build();
app.UseLabDefaults();   // X-Served-By / X-Trace-Id headers, request log, graceful shutdown

await app.MigrateDatabaseAsync<BillingDbContext>();

app.MapGrpcService<BillingGrpcService>();

var api = app.MapGroup("/api/billing");

api.MapHealthChecks("/health");

api.MapGet("/invoices", async (BillingDbContext db) =>
    await db.Invoices.AsNoTracking().OrderByDescending(i => i.Id).ToListAsync());

api.MapGet("/invoices/{id:int}", async (int id, BillingDbContext db) =>
    await db.Invoices.FindAsync(id) is { } invoice ? Results.Ok(invoice) : Results.NotFound());

// Fake payment gateway: approves every card. In the real system this would call an external provider.
api.MapPost("/invoices/{id:int}/pay", async (int id, PaymentRequest request, BillingDbContext db, ILogger<Program> logger) =>
{
    if (await db.Invoices.FindAsync(id) is not { } invoice)
        return Results.NotFound();
    if (invoice.Status == InvoiceStatus.Paid)
        return Results.Conflict($"Invoice {id} is already paid.");
    if (string.IsNullOrWhiteSpace(request.CardNumber))
        return Results.BadRequest("CardNumber is required.");

    invoice.Status = InvoiceStatus.Paid;
    invoice.PaidAt = DateTime.UtcNow;
    invoice.PaymentReference = $"FAKE-{Guid.NewGuid():N}"[..20];
    await db.SaveChangesAsync();
    logger.LogInformation("Invoice {InvoiceId} paid, reference {Reference}", invoice.Id, invoice.PaymentReference);
    return Results.Ok(new { approved = true, invoice.PaymentReference, invoice });
});

app.Run();

record PaymentRequest(string CardNumber);
