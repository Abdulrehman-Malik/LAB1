namespace DocumentsApi.Data;

/// <summary>
/// A pointer to a file. The bytes live in MinIO under <see cref="ObjectKey"/>, never in SQL.
/// The two stores must stay in sync: a row without its object (or the other way round) is a broken document.
/// </summary>
public class Document
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string Bucket { get; set; }
    public required string ObjectKey { get; set; }   // e.g. orders/12/3f2a.../invoice.pdf
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
