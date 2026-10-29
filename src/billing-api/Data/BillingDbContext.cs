using Microsoft.EntityFrameworkCore;

namespace BillingApi.Data;

/// <summary>Lives in the shared SalesDb, inside its own "billing" schema (orders-api owns the "orders" schema).</summary>
public class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public const string Schema = "billing";

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        var invoice = modelBuilder.Entity<Invoice>();
        invoice.Property(i => i.Amount).HasPrecision(18, 2);
        invoice.Property(i => i.Status).HasMaxLength(20);
        invoice.Property(i => i.PaymentReference).HasMaxLength(50);
        invoice.HasIndex(i => i.OrderId).IsUnique();   // one invoice per order
    }
}
