namespace DocProcessing.Contracts.Commands;

public sealed record RunOcrCommand(
    Guid DocumentId,
    string BlobPath);
