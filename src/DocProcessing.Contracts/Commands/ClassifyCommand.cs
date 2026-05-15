namespace DocProcessing.Contracts.Commands;

public sealed record ClassifyCommand(
    Guid DocumentId,
    string OcrResultBlobPath);
