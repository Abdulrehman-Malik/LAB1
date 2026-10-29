using DocumentsApi;
using DocumentsApi.Data;
using Lab.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Secrets from /run/secrets (Database__Password, Storage__SecretKey), REST on 8080, lab log format
builder.AddLabDefaults();

builder.Services.AddLabDbContext<DocumentsDbContext>(builder.Configuration, "DocumentsDb");
builder.Services.AddSingleton<FileStorage>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DocumentsDbContext>()
    .AddCheck<MinioHealthCheck>("minio");

var app = builder.Build();
app.UseLabDefaults();   // X-Served-By / X-Trace-Id headers, request log, graceful shutdown

await app.MigrateDatabaseAsync<DocumentsDbContext>();
await app.Services.GetRequiredService<FileStorage>().EnsureBucketAsync();

var api = app.MapGroup("/api/documents");

api.MapHealthChecks("/health");

// List documents, optionally only those attached to one order: /api/documents?orderId=12
api.MapGet("", async (int? orderId, DocumentsDbContext db) =>
    await db.Documents.AsNoTracking()
        .Where(d => orderId == null || d.OrderId == orderId)
        .OrderByDescending(d => d.Id)
        .ToListAsync());

// Upload: multipart form with "file" and an optional "orderId".
// 1) bytes -> MinIO, 2) a row with the object key -> SQL. If step 2 fails, step 1 is undone.
api.MapPost("", async (IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] int? orderId,
    DocumentsDbContext db, FileStorage storage, ILogger<Program> logger, CancellationToken ct) =>
{
    if (file.Length == 0)
        return Results.BadRequest("The file is empty.");

    var key = $"orders/{orderId?.ToString() ?? "none"}/{Guid.NewGuid():N}/{Path.GetFileName(file.FileName)}";
    await using (var content = file.OpenReadStream())
        await storage.UploadAsync(key, content, file.ContentType, ct);

    var document = new Document
    {
        OrderId = orderId,
        FileName = Path.GetFileName(file.FileName),
        ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
        SizeBytes = file.Length,
        Bucket = storage.Bucket,
        ObjectKey = key,
    };
    try
    {
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);
    }
    catch
    {
        await storage.DeleteAsync(key, CancellationToken.None);   // don't leave an orphan file behind
        throw;
    }

    logger.LogInformation("Document {DocumentId} uploaded: {FileName} ({Size} bytes) for order {OrderId}",
        document.Id, document.FileName, document.SizeBytes, orderId);
    return Results.Created($"/api/documents/{document.Id}", document);
}).DisableAntiforgery();   // a plain API (no cookies/forms from other sites), so no antiforgery token

api.MapGet("/{id:int}/download", async (int id, DocumentsDbContext db, FileStorage storage, CancellationToken ct) =>
{
    if (await db.Documents.FindAsync([id], ct) is not { } document)
        return Results.NotFound($"Document {id} does not exist in DocumentsDb.");

    var stream = await storage.DownloadAsync(document.ObjectKey, ct);
    return stream is null
        ? Results.Problem($"Document {id} exists in SQL but its file is missing from MinIO ({document.ObjectKey}).", statusCode: 404)
        : Results.Stream(stream, document.ContentType, document.FileName);
});

api.MapDelete("/{id:int}", async (int id, DocumentsDbContext db, FileStorage storage, ILogger<Program> logger, CancellationToken ct) =>
{
    if (await db.Documents.FindAsync([id], ct) is not { } document)
        return Results.NotFound();

    await storage.DeleteAsync(document.ObjectKey, ct);
    db.Documents.Remove(document);
    await db.SaveChangesAsync(ct);
    logger.LogInformation("Document {DocumentId} deleted", id);
    return Results.NoContent();
});

app.Run();

/// <summary>documents-api is only healthy if it can reach MinIO as well as SQL Server.</summary>
class MinioHealthCheck(FileStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await storage.IsReachableAsync()
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Bucket {storage.Bucket} not found");
        }
        catch (Exception ex)
        {
            // Message only, no exception: SDK stack traces are long and bury the useful log lines
            return HealthCheckResult.Unhealthy($"MinIO unreachable: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
