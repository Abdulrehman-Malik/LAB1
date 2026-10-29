using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace DocumentsApi;

/// <summary>
/// Talks to MinIO with the standard Amazon S3 SDK: MinIO speaks the S3 protocol, so the same code would work
/// against AWS S3 by changing Storage:Endpoint and the keys. Bucket = top-level folder, key = file path inside it.
/// </summary>
public class FileStorage
{
    private readonly IAmazonS3 s3;
    private readonly ILogger<FileStorage> logger;

    public string Bucket { get; }

    public FileStorage(IConfiguration config, ILogger<FileStorage> logger)
    {
        var storage = config.GetSection("Storage");
        Bucket = storage["Bucket"] ?? "documents";
        this.logger = logger;
        s3 = new AmazonS3Client(
            new BasicAWSCredentials(storage["AccessKey"], storage["SecretKey"]),
            new AmazonS3Config
            {
                ServiceURL = storage["Endpoint"],   // e.g. http://storage-minio:9000
                ForcePathStyle = true,              // http://host/bucket/key (MinIO) instead of http://bucket.host/key (AWS)
                AuthenticationRegion = "us-east-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    /// <summary>Creates the bucket on first start, retrying while MinIO is still starting.</summary>
    public async Task EnsureBucketAsync(int attempts = 20)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(s3, Bucket))
                {
                    await s3.PutBucketAsync(Bucket);
                    logger.LogInformation("Created bucket {Bucket}", Bucket);
                }
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                logger.LogWarning("MinIO not ready ({Attempt}/{Max}): {Message}", attempt, attempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
        }, ct);
        logger.LogInformation("Stored object {Bucket}/{Key}", Bucket, key);
    }

    /// <summary>Returns the object's stream, or null if it is missing from MinIO (e.g. deleted in the MinIO console).</summary>
    public async Task<Stream?> DownloadAsync(string key, CancellationToken ct)
    {
        try
        {
            var response = await s3.GetObjectAsync(Bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("Object {Bucket}/{Key} is missing: the SQL row points to a file that no longer exists", Bucket, key);
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await s3.DeleteObjectAsync(Bucket, key, ct);
        logger.LogInformation("Deleted object {Bucket}/{Key}", Bucket, key);
    }

    /// <summary>Used by the health check: can we reach MinIO and see our bucket?</summary>
    public Task<bool> IsReachableAsync() => Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(s3, Bucket);
}
