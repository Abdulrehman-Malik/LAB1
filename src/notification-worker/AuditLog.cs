using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace NotificationWorker;

/// <summary>One document in MongoDB's lab.audit collection. Documents don't need the same shape (Detail is optional).</summary>
public class AuditEvent
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public required string Event { get; set; }   // InvoiceEmailSent | InvoiceEmailFailed
    public int InvoiceId { get; set; }
    public int OrderId { get; set; }
    public string? Recipient { get; set; }
    public string? Detail { get; set; }
    public string? TraceId { get; set; }          // paste into: bash scripts/trace.sh <id>
    public string Worker { get; set; } = Environment.MachineName;
    public DateTime At { get; set; } = DateTime.UtcNow;
}

/// <summary>Append-only audit trail in MongoDB: what the worker did, when, and for whom.</summary>
public class AuditLog
{
    private readonly IMongoDatabase database;
    private readonly IMongoCollection<AuditEvent> events;

    public AuditLog(IConfiguration config)
    {
        var mongo = config.GetSection("Mongo");
        var url = $"mongodb://{mongo["User"]}:{Uri.EscapeDataString(mongo["Password"] ?? "")}@{mongo["Host"]}:{mongo["Port"] ?? "27017"}/?authSource=admin";
        database = new MongoClient(url).GetDatabase(mongo["Database"] ?? "lab");
        events = database.GetCollection<AuditEvent>("audit");
    }

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct) => events.InsertOneAsync(auditEvent, cancellationToken: ct);

    public Task<List<AuditEvent>> RecentAsync(int count, CancellationToken ct) =>
        events.Find(FilterDefinition<AuditEvent>.Empty).SortByDescending(e => e.At).Limit(count).ToListAsync(ct);

    public Task PingAsync(CancellationToken ct) => database.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }", cancellationToken: ct);
}
