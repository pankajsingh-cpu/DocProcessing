using DocProcessing.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Options;

namespace DocProcessing.Ingest.Service;

// One landed file = one document. Whether it's a single-page TIF, a multi-page
// TIF, or a multi-page PDF, the file is uploaded as-is to the blob store and a
// single DocumentIngestedEvent is published. The OCR worker
// (Azure.AI.DocumentIntelligence) handles multi-page input in one analyze call.
//
// The build guide flagged the previous per-page-TIF splitting rule as a
// placeholder for future barcode/separator detection; that splitting logic
// belongs in a downstream "DocumentClassifier-aware splitter" service rather
// than in ingest. For now the unit of ingestion is the file.
public sealed class BatchArrivedConsumer(
    IBlobUploader uploader,
    IOptions<IngestOptions> opts,
    ILogger<BatchArrivedConsumer> logger)
    : IConsumer<BatchArrivedEvent>
{
    private readonly IngestOptions _opts = opts.Value;
    private readonly HashSet<string> _supportedExtensions = new(
        opts.Value.SupportedExtensions.Select(NormaliseExtension),
        StringComparer.OrdinalIgnoreCase);

    public async Task Consume(ConsumeContext<BatchArrivedEvent> ctx)
    {
        var batch = ctx.Message;
        var sourceFileName = Path.GetFileName(batch.SourcePath);
        var ext = NormaliseExtension(Path.GetExtension(batch.SourcePath));

        if (!_supportedExtensions.Contains(ext))
        {
            logger.LogWarning(
                "Ignoring batch {BatchId}: extension '{Extension}' is not in SupportedExtensions ({Supported})",
                batch.BatchId, ext, string.Join(",", _supportedExtensions));
            return;
        }

        var docId = Guid.NewGuid();
        var blobPath = $"{_opts.DocumentPrefix.TrimEnd('/')}/{docId}{ext}";

        logger.LogInformation(
            "Ingesting batch {BatchId} from {Source} as {BlobPath}",
            batch.BatchId, batch.SourcePath, blobPath);

        var bytes = await File.ReadAllBytesAsync(batch.SourcePath, ctx.CancellationToken);
        await uploader.UploadAsync(blobPath, bytes, ctx.CancellationToken);

        await ctx.Publish(new DocumentIngestedEvent(
            DocumentId: docId,
            BatchId: batch.BatchId,
            BlobPath: blobPath,
            SourceFileName: sourceFileName,
            IngestedAt: DateTimeOffset.UtcNow));

        logger.LogInformation(
            "Ingested batch {BatchId}: 1 document ({Bytes} bytes)",
            batch.BatchId, bytes.Length);
    }

    private static string NormaliseExtension(string ext)
    {
        if (string.IsNullOrEmpty(ext)) return string.Empty;
        return ext.StartsWith('.') ? ext : "." + ext;
    }
}
