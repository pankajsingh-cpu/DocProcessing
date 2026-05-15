using Azure.Storage.Blobs;
using DocProcessing.Contracts.Commands;
using DocProcessing.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Options;

namespace DocProcessing.Ocr;

public sealed class RunOcrConsumer(
    IOcrEngine engine,
    BlobServiceClient blobService,
    IOptions<OcrOptions> opts,
    ILogger<RunOcrConsumer> logger) : IConsumer<RunOcrCommand>
{
    public async Task Consume(ConsumeContext<RunOcrCommand> ctx)
    {
        var cmd = ctx.Message;
        var container = blobService.GetBlobContainerClient(opts.Value.ContainerName);
        var ocrPrefix = opts.Value.OcrPrefix.TrimEnd('/');
        var ocrBlobPath = $"{ocrPrefix}/{cmd.DocumentId}.json";

        try
        {
            var sourceBlob = container.GetBlobClient(cmd.BlobPath);
            var download = await sourceBlob.DownloadContentAsync(ctx.CancellationToken);
            var sourceBytes = download.Value.Content.ToArray();

            var result = await engine.AnalyseAsync(cmd.DocumentId, sourceBytes, ctx.CancellationToken);

            await container.GetBlobClient(ocrBlobPath).UploadAsync(
                BinaryData.FromBytes(result.JsonPayload),
                overwrite: true,
                cancellationToken: ctx.CancellationToken);

            await ctx.Publish(new OcrCompletedEvent(
                DocumentId: cmd.DocumentId,
                OcrResultBlobPath: ocrBlobPath,
                PageCount: result.PageCount,
                CompletedAt: DateTimeOffset.UtcNow));

            logger.LogInformation("OCR completed for {DocumentId} -> {OcrPath} ({Pages} pages)",
                cmd.DocumentId, ocrBlobPath, result.PageCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OCR failed for {DocumentId}", cmd.DocumentId);
            await ctx.Publish(new DocumentFailedEvent(
                DocumentId: cmd.DocumentId,
                Stage: "ocr",
                Error: ex.Message,
                StackTrace: ex.StackTrace));
        }
    }
}
