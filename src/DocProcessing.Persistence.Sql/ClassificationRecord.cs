namespace DocProcessing.Persistence.Sql;

// One row per processed document. Mirrors the ClassificationResultsStore on the
// C4 L3 diagram. Intents + ExtractedFields are JSON payloads so the schema
// doesn't need to evolve when new intents or fields are added.
public sealed class ClassificationRecord
{
    public Guid DocumentId { get; set; }

    // Highest-confidence intent name from the classifier output. Indexed in
    // OnModelCreating so dashboards can slice by intent without scanning JSON.
    public string TransactionType { get; set; } = default!;

    public double Confidence { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    // JSON-serialised IReadOnlyList<IntentResult> from the ClassificationCompletedEvent.
    public string Intents { get; set; } = default!;

    // JSON-serialised merged dictionary of extracted fields across all intents.
    public string ExtractedFields { get; set; } = default!;
}
