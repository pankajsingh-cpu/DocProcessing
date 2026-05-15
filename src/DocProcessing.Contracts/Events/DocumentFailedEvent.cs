namespace DocProcessing.Contracts.Events;

public sealed record DocumentFailedEvent(
    Guid DocumentId,
    string Stage,
    string Error,
    string? StackTrace);
