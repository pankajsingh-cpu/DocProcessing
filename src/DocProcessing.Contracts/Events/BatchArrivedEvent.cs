namespace DocProcessing.Contracts.Events;

public sealed record BatchArrivedEvent(
    string BatchId,
    string SourcePath,
    DateTimeOffset ArrivedAt);
