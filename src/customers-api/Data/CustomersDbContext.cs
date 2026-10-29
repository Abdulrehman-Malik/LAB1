using Microsoft.EntityFrameworkCore;

namespace CustomersApi.Data;

public class CustomersDbContext(DbContextOptions<CustomersDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var customer = modelBuilder.Entity<Customer>();
        customer.Property(c => c.Name).HasMaxLength(100);
        customer.Property(c => c.Email).HasMaxLength(200);
        customer.Property(c => c.City).HasMaxLength(100);
        customer.HasIndex(c => c.Email).IsUnique();

        // Seed data is part of the migration, so every environment starts with the same customers
        var seeded = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        customer.HasData(
            new Customer { Id = 1, Name = "Ali Khan", Email = "ali@lab.local", City = "Lahore", CreatedAt = seeded },
            new Customer { Id = 2, Name = "Sara Ahmed", Email = "sara@lab.local", City = "Karachi", CreatedAt = seeded },
            new Customer { Id = 3, Name = "Usman Tariq", Email = "usman@lab.local", City = "Islamabad", CreatedAt = seeded });
    }
}
