namespace DocProcessing.Contracts.Events;

public sealed record ClassificationCompletedEvent(
    Guid DocumentId,
    IReadOnlyList<IntentResult> Intents,
    DateTimeOffset CompletedAt);
