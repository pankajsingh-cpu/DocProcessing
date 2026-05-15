namespace DocProcessing.Ocr;

public interface IOcrEngine
{
    Task<OcrResult> AnalyseAsync(Guid documentId, byte[] sourceBytes, CancellationToken ct);
}

public sealed record OcrResult(int PageCount, byte[] JsonPayload);
