namespace DocProcessing.Contracts.Events;

public sealed record OcrCompletedEvent(
    Guid DocumentId,
    string OcrResultBlobPath,
    int PageCount,
    DateTimeOffset CompletedAt);
