using System.ClientModel;
using Azure.Storage.Blobs;
using DocProcessing.Contracts.Commands;
using DocProcessing.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Options;

namespace DocProcessing.Classifier;

public sealed class ClassifyConsumer(
    IIntentClassifier classifier,
    BlobServiceClient blobService,
    IOptions<ClassifierOptions> options,
    ILogger<ClassifyConsumer> logger) : IConsumer<ClassifyCommand>
{
    private readonly ClassifierOptions _options = options.Value;

    public async Task Consume(ConsumeContext<ClassifyCommand> ctx)
    {
        var cmd = ctx.Message;
        var container = blobService.GetBlobContainerClient(_options.ContainerName);

        try
        {
            var download = await container.GetBlobClient(cmd.OcrResultBlobPath)
                .DownloadContentAsync(ctx.CancellationToken);

            var ocrText = OcrTextFlattener.Flatten(download.Value.Content);
            if (string.IsNullOrWhiteSpace(ocrText))
            {
                throw new InvalidOperationException(
                    $"OCR JSON at {cmd.OcrResultBlobPath} flattened to empty text — cannot classify.");
            }

            var output = await classifier.ClassifyAsync(ocrText, ctx.CancellationToken);

            var intents = output.Intents
                .Select(i => new IntentResult(
                    IntentName: i.Intent,
                    Confidence: i.Confidence,
                    Payload: i.Payload))
                .ToList();

            await ctx.Publish(new ClassificationCompletedEvent(
                DocumentId: cmd.DocumentId,
                Intents: intents,
                CompletedAt: DateTimeOffset.UtcNow));

            logger.LogInformation(
                "Classification complete for {DocumentId}: {Count} intent(s) — {Intents}",
                cmd.DocumentId, intents.Count, string.Join(", ", intents.Select(i => i.IntentName)));
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            logger.LogError(ex,
                "Classifier exhausted rate-limit retries for {DocumentId}", cmd.DocumentId);
            await ctx.Publish(new DocumentFailedEvent(
                DocumentId: cmd.DocumentId,
                Stage: "rate-limited",
                Error: ex.Message,
                StackTrace: ex.StackTrace));
        }
        catch (ClassifierJsonException ex)
        {
            logger.LogError(ex,
                "Classifier returned unparseable JSON for {DocumentId}; " +
                "first={First} second={Second}",
                cmd.DocumentId, ex.FirstResponse, ex.SecondResponse);
            await ctx.Publish(new DocumentFailedEvent(
                DocumentId: cmd.DocumentId,
                Stage: "classifier-bad-json",
                Error: ex.Message,
                StackTrace: ex.StackTrace));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Classifier failed for {DocumentId}", cmd.DocumentId);
            await ctx.Publish(new DocumentFailedEvent(
                DocumentId: cmd.DocumentId,
                Stage: "classifier",
                Error: ex.Message,
                StackTrace: ex.StackTrace));
        }
    }
}
