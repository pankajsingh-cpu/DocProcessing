using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DocProcessing.Common.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocProcessing.Persistence.Archive;

public sealed class ArchiveService(
    BlobServiceClient blobService,
    IOptions<ArchiveOptions> options,
    TimeProvider timeProvider,
    ILogger<ArchiveService> logger) : IArchiveService
{
    private readonly ArchiveOptions _options = options.Value;

    public async Task ArchiveAsync(Guid documentId, string sourceBlobPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceBlobPath))
        {
            throw new ArgumentException("Source blob path is required.", nameof(sourceBlobPath));
        }

        var container = blobService.GetBlobContainerClient(_options.ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var source = container.GetBlobClient(sourceBlobPath);

        if (!await source.ExistsAsync(ct))
        {
            // We log a warning instead of throwing: the source blob may legitimately
            // be missing in dev (stub OCR runs skip the ingest stage). The saga
            // still considers the document persisted — the SQL row is authoritative.
            logger.LogWarning(
                "Archive skipped: source blob {Source} does not exist for {DocumentId}",
                sourceBlobPath, documentId);
            return;
        }

        // Preserve the original file extension (.tif, .tiff, .pdf, ...).
        var ext = Path.GetExtension(sourceBlobPath);
        var now = timeProvider.GetUtcNow();
        var destPath = $"{_options.ArchivePrefix.TrimEnd('/')}/{now:yyyy}/{now:MM}/{documentId}{ext}";
        var destination = container.GetBlobClient(destPath);

        if (await destination.ExistsAsync(ct))
        {
            logger.LogInformation(
                "Archive already present at {Destination} for {DocumentId}; skipping copy",
                destPath, documentId);
            return;
        }

        // Server-side copy — works against Azurite + real Blob storage.
        var copyOp = await destination.StartCopyFromUriAsync(source.Uri, cancellationToken: ct);
        var copyStarted = timeProvider.GetUtcNow();

        while (true)
        {
            var props = (await destination.GetPropertiesAsync(cancellationToken: ct)).Value;
            switch (props.CopyStatus)
            {
                case CopyStatus.Success:
                    logger.LogInformation(
                        "Archived {DocumentId}: {Source} -> {Destination}",
                        documentId, sourceBlobPath, destPath);
                    return;
                case CopyStatus.Failed:
                case CopyStatus.Aborted:
                    throw new InvalidOperationException(
                        $"Archive copy {props.CopyStatus} for {documentId}: {props.CopyStatusDescription}");
            }

            if (timeProvider.GetUtcNow() - copyStarted > _options.CopyTimeout)
            {
                throw new TimeoutException(
                    $"Archive copy did not complete within {_options.CopyTimeout} for {documentId}");
            }

            await Task.Delay(_options.CopyPollInterval, timeProvider, ct);
        }
    }
}
