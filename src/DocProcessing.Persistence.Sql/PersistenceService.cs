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
        var top = ChooseHeadlineIntent(evt.Intents);
        var transactionType = top?.IntentName ?? "unknown";
        var confidence = top?.Confidence ?? 0d;

        // ExtractedFields column holds the top-confidence intent's typed
        // payload JSON verbatim (drip_ocp / sell_stock shape, etc.). The
        // Intents column carries the full list including all candidates.
        var topPayloadJson = top is null
            ? "{}"
            : JsonSerializer.Serialize(top.Payload, JsonOpts.StrictCamelCase);

        var intentsJson = JsonSerializer.Serialize(evt.Intents, JsonOpts.StrictCamelCase);

        var existing = await db.ClassificationRecords.FindAsync(new object?[] { evt.DocumentId }, ct);
        if (existing is null)
        {
            db.ClassificationRecords.Add(new ClassificationRecord
            {
                DocumentId = evt.DocumentId,
                TransactionType = transactionType,
                Confidence = confidence,
                CreatedDate = evt.CompletedAt,
                Intents = intentsJson,
                ExtractedFields = topPayloadJson,
            });
        }
        else
        {
            // Idempotency: a retried saga shouldn't double-write. Update in
            // place so the latest classifier output wins.
            existing.TransactionType = transactionType;
            existing.Confidence = confidence;
            existing.CreatedDate = evt.CompletedAt;
            existing.Intents = intentsJson;
            existing.ExtractedFields = topPayloadJson;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Persisted classification for {DocumentId}: {Transaction} (confidence {Confidence:P0})",
            evt.DocumentId, transactionType, confidence);
    }

    private static IntentResult? ChooseHeadlineIntent(IReadOnlyList<IntentResult> intents) =>
        intents.Count == 0 ? null : intents.OrderByDescending(i => i.Confidence).First();
}
