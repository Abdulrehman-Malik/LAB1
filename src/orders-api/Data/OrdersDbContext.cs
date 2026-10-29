using Microsoft.EntityFrameworkCore;

namespace OrdersApi.Data;

/// <summary>Lives in the shared SalesDb, inside its own "orders" schema (billing-api owns the "billing" schema).</summary>
public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public const string Schema = "orders";

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        var order = modelBuilder.Entity<Order>();
        order.Property(o => o.Product).HasMaxLength(200);
        order.Property(o => o.UnitPrice).HasPrecision(18, 2);
        order.Property(o => o.Amount).HasPrecision(18, 2);
        order.Property(o => o.Status).HasMaxLength(20);
        order.HasIndex(o => o.CustomerId);
    }
}
