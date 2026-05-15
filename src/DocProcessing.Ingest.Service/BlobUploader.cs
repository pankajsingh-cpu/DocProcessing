using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace DocProcessing.Ingest.Service;

public interface IBlobUploader
{
    Task UploadAsync(string blobPath, byte[] content, CancellationToken ct);
}

public sealed class BlobUploader : IBlobUploader
{
    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(200),
            ShouldHandle = new PredicateBuilder()
                .Handle<RequestFailedException>()
                .Handle<IOException>(),
        })
        .Build();

    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobUploader> _logger;

    public BlobUploader(
        BlobServiceClient serviceClient,
        IOptions<IngestOptions> opts,
        ILogger<BlobUploader> logger)
    {
        _container = serviceClient.GetBlobContainerClient(opts.Value.ContainerName);
        _logger = logger;
    }

    public async Task UploadAsync(string blobPath, byte[] content, CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = _container.GetBlobClient(blobPath);

        await RetryPipeline.ExecuteAsync(async cancel =>
        {
            using var ms = new MemoryStream(content, writable: false);
            await blob.UploadAsync(ms, overwrite: true, cancellationToken: cancel);
            _logger.LogInformation("Uploaded blob {BlobPath} ({Bytes} bytes)", blobPath, content.Length);
        }, ct);
    }
}
