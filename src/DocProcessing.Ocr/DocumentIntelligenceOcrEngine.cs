using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace DocProcessing.Ocr;

// Real Azure Document Intelligence engine. prebuilt-layout returns AnalyzeResult,
// which we serialise to JSON (preserving full layout for downstream classifier).
public sealed class DocumentIntelligenceOcrEngine(
    DocumentIntelligenceClient client,
    IOptions<DocumentIntelligenceOptions> opts,
    ILogger<DocumentIntelligenceOcrEngine> logger) : IOcrEngine
{
    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(1),
            ShouldHandle = new PredicateBuilder().Handle<RequestFailedException>(),
        })
        .Build();

    public async Task<OcrResult> AnalyseAsync(Guid documentId, byte[] sourceBytes, CancellationToken ct)
    {
        var modelId = opts.Value.ModelId;
        logger.LogInformation("Running DI model {ModelId} for {DocumentId} ({Bytes} bytes)",
            modelId, documentId, sourceBytes.Length);

        var operation = await RetryPipeline.ExecuteAsync(async cancel =>
            await client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                modelId,
                BinaryData.FromBytes(sourceBytes),
                cancellationToken: cancel), ct);

        var result = operation.Value;

        // Serialize the AnalyzeResult to JSON. Cast through BinaryData/JsonElement.
        var json = BinaryData.FromObjectAsJson(result);
        var pageCount = result.Pages?.Count ?? 0;

        return new OcrResult(pageCount, json.ToArray());
    }
}
