using Microsoft.EntityFrameworkCore;

namespace DocumentsApi.Data;

public class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<Document>();
        document.Property(d => d.FileName).HasMaxLength(255);
        document.Property(d => d.ContentType).HasMaxLength(100);
        document.Property(d => d.Bucket).HasMaxLength(63);
        document.Property(d => d.ObjectKey).HasMaxLength(500);
        document.HasIndex(d => d.OrderId);
    }
}
