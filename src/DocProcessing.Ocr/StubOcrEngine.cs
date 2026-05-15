using System.Text.Json;
using DocProcessing.Contracts;
using Microsoft.Extensions.Options;

namespace DocProcessing.Ocr;

// Returns canned OCR JSON from samples/ocr-fixtures so the pipeline runs without a
// real Document Intelligence resource. Used when OCR_MODE=stub.
public sealed class StubOcrEngine(
    IOptions<OcrOptions> opts,
    ILogger<StubOcrEngine> logger) : IOcrEngine
{
    private readonly OcrOptions _opts = opts.Value;

    public async Task<OcrResult> AnalyseAsync(Guid documentId, byte[] sourceBytes, CancellationToken ct)
    {
        var root = Path.GetFullPath(_opts.FixturePath);
        var specific = Path.Combine(root, $"{documentId}.json");
        var generic = Path.Combine(root, _opts.GenericFixtureName);

        var chosenPath = File.Exists(specific) ? specific : generic;

        byte[] bytes;
        if (File.Exists(chosenPath))
        {
            bytes = await File.ReadAllBytesAsync(chosenPath, ct);
            logger.LogInformation("Stub OCR loaded fixture {Path} for {DocumentId}", chosenPath, documentId);
        }
        else
        {
            bytes = BuildInlineGenericFixture();
            logger.LogWarning(
                "Stub OCR fixture not found at {Path}; using built-in 1-page fixture for {DocumentId}",
                chosenPath, documentId);
        }

        var pageCount = CountPages(bytes);
        return new OcrResult(pageCount, bytes);
    }

    private static byte[] BuildInlineGenericFixture()
    {
        var doc = new
        {
            modelId = "stub-prebuilt-layout",
            pages = new[]
            {
                new
                {
                    pageNumber = 1,
                    content = "STUB OCR PAGE 1 — replace samples/ocr-fixtures/_generic.json or {documentId}.json with a real layout response.",
                },
            },
        };
        return JsonSerializer.SerializeToUtf8Bytes(doc, JsonOpts.StrictCamelCase);
    }

    private static int CountPages(byte[] jsonBytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBytes);
            if (doc.RootElement.TryGetProperty("pages", out var pages) &&
                pages.ValueKind == JsonValueKind.Array)
            {
                return pages.GetArrayLength();
            }
        }
        catch (JsonException)
        {
            // Fall through to default.
        }
        return 1;
    }
}
