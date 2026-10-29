using CustomersApi.Data;
using CustomersApi.Grpc;
using Lab.Shared;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Secrets from /run/secrets, REST on 8080, gRPC on 8081 (see Lab.Shared)
builder.AddLabDefaults(grpc: true);

builder.Services.AddLabDbContext<CustomersDbContext>(builder.Configuration, "CustomersDb");
builder.Services.AddGrpc();
builder.Services.AddHealthChecks().AddDbContextCheck<CustomersDbContext>();

var app = builder.Build();
app.UseLabDefaults();   // X-Served-By / X-Trace-Id headers, request log, graceful shutdown

await app.MigrateDatabaseAsync<CustomersDbContext>();

app.MapGrpcService<CustomerGrpcService>();

var api = app.MapGroup("/api/customers");

api.MapHealthChecks("/health");

// Optional filter: /api/customers?city=Lahore
api.MapGet("", async (string? city, CustomersDbContext db) =>
    await db.Customers.AsNoTracking()
        .Where(c => city == null || c.City == city)
        .OrderBy(c => c.Id)
        .ToListAsync());

api.MapGet("/{id:int}", async (int id, CustomersDbContext db) =>
    await db.Customers.FindAsync(id) is { } customer ? Results.Ok(customer) : Results.NotFound());

api.MapPost("", async (CreateCustomerRequest request, CustomersDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
        return Results.BadRequest("Name and Email are required.");

    var customer = new Customer { Name = request.Name, Email = request.Email, City = request.City ?? "" };
    db.Customers.Add(customer);
    await db.SaveChangesAsync();
    return Results.Created($"/api/customers/{customer.Id}", customer);
});

app.Run();

record CreateCustomerRequest(string Name, string Email, string? City);
