using ImageMagick;

namespace DocProcessing.Ingest.Service;

public interface ITifSplitter
{
    // TODO(cloud-iter-2): replace one-page-per-document with barcode / separator-page
    // detection. Pass-through interface stays the same.
    IAsyncEnumerable<DocumentPage> SplitAsync(Stream source, CancellationToken ct);
}

public sealed record DocumentPage(int PageNumber, byte[] TifBytes);

public sealed class TifSplitter : ITifSplitter
{
    public async IAsyncEnumerable<DocumentPage> SplitAsync(
        Stream source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var collection = new MagickImageCollection();
        await collection.ReadAsync(source, MagickFormat.Tiff, ct);

        var pageNo = 0;
        foreach (var page in collection)
        {
            ct.ThrowIfCancellationRequested();
            pageNo++;

            page.Format = MagickFormat.Tiff;
            using var ms = new MemoryStream();
            await page.WriteAsync(ms, ct);
            yield return new DocumentPage(pageNo, ms.ToArray());
        }
    }
}
