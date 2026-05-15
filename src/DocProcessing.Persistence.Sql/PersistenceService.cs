using System.Text.Json;
using DocProcessing.Common.Persistence;
using DocProcessing.Contracts;
using DocProcessing.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocProcessing.Persistence.Sql;

public sealed class PersistenceService(
    PersistenceDbContext db,
    ILogger<PersistenceService> logger) : IPersistenceService
{
    public async Task SaveClassificationAsync(ClassificationCompletedEvent evt, CancellationToken ct)
    {
        var (transactionType, confidence) = ChooseHeadlineIntent(evt.Intents);
        var extractedFields = MergeExtractedFields(evt.Intents);

        var existing = await db.ClassificationRecords.FindAsync(new object?[] { evt.DocumentId }, ct);
        if (existing is null)
        {
            db.ClassificationRecords.Add(new ClassificationRecord
            {
                DocumentId = evt.DocumentId,
                TransactionType = transactionType,
                Confidence = confidence,
                CreatedDate = evt.CompletedAt,
                Intents = JsonSerializer.Serialize(evt.Intents, JsonOpts.StrictCamelCase),
                ExtractedFields = JsonSerializer.Serialize(extractedFields, JsonOpts.StrictCamelCase),
            });
        }
        else
        {
            // Idempotency: a retried saga shouldn't double-write. Update in
            // place so the latest classifier output wins.
            existing.TransactionType = transactionType;
            existing.Confidence = confidence;
            existing.CreatedDate = evt.CompletedAt;
            existing.Intents = JsonSerializer.Serialize(evt.Intents, JsonOpts.StrictCamelCase);
            existing.ExtractedFields = JsonSerializer.Serialize(extractedFields, JsonOpts.StrictCamelCase);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Persisted classification for {DocumentId}: {Transaction} (confidence {Confidence:P0})",
            evt.DocumentId, transactionType, confidence);
    }

    private static (string TransactionType, double Confidence) ChooseHeadlineIntent(
        IReadOnlyList<IntentResult> intents)
    {
        if (intents.Count == 0)
        {
            return ("unknown", 0d);
        }

        var top = intents.OrderByDescending(i => i.Confidence).First();
        return (top.IntentName, top.Confidence);
    }

    private static IDictionary<string, string?> MergeExtractedFields(IReadOnlyList<IntentResult> intents)
    {
        // Higher-confidence intents win on overlapping keys.
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var intent in intents.OrderBy(i => i.Confidence))
        {
            foreach (var (key, value) in intent.ExtractedFields)
            {
                merged[key] = value;
            }
        }
        return merged;
    }
}
