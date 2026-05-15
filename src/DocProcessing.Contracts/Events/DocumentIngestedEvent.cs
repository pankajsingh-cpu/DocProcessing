namespace DocProcessing.Contracts.Events;

public sealed record DocumentIngestedEvent(
    Guid DocumentId,
    string BatchId,
    string BlobPath,
    string SourceFileName,
    DateTimeOffset IngestedAt);
