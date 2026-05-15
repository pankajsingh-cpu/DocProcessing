using DocProcessing.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Options;

namespace DocProcessing.Ingest.Service;

public sealed class BatchArrivedConsumer(
    ITifSplitter splitter,
    IBlobUploader uploader,
    IOptions<IngestOptions> opts,
    ILogger<BatchArrivedConsumer> logger)
    : IConsumer<BatchArrivedEvent>
{
    public async Task Consume(ConsumeContext<BatchArrivedEvent> ctx)
    {
        var batch = ctx.Message;
        var sourceFileName = Path.GetFileName(batch.SourcePath);
        var prefix = opts.Value.DocumentPrefix.TrimEnd('/');

        logger.LogInformation(
            "Splitting batch {BatchId} from {Source}",
            batch.BatchId, batch.SourcePath);

        await using var src = File.OpenRead(batch.SourcePath);

        var pageCount = 0;
        await foreach (var page in splitter.SplitAsync(src, ctx.CancellationToken))
        {
            var docId = Guid.NewGuid();
            var blobPath = $"{prefix}/{docId}.tif";

            await uploader.UploadAsync(blobPath, page.TifBytes, ctx.CancellationToken);

            await ctx.Publish(new DocumentIngestedEvent(
                DocumentId: docId,
                BatchId: batch.BatchId,
                BlobPath: blobPath,
                SourceFileName: sourceFileName,
                IngestedAt: DateTimeOffset.UtcNow));

            pageCount++;
        }

        logger.LogInformation(
            "Ingested batch {BatchId}: {Pages} document(s)",
            batch.BatchId, pageCount);
    }
}
