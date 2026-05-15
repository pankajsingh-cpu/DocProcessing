using ImageMagick;

namespace DocProcessing.Ingest.Service.Tests;

internal static class TifFixtures
{
    public static byte[] CreateMultiPageTif(int pageCount, int width = 64, int height = 64)
    {
        using var collection = new MagickImageCollection();
        for (var i = 0; i < pageCount; i++)
        {
            // Alternate colors so each page is visually distinct (useful when manually inspecting failed runs).
            var bg = i % 2 == 0 ? MagickColors.White : MagickColors.LightGray;
            var page = new MagickImage(bg, (uint)width, (uint)height) { Format = MagickFormat.Tiff };
            collection.Add(page);
        }
        using var ms = new MemoryStream();
        collection.Write(ms, MagickFormat.Tiff);
        return ms.ToArray();
    }

    public static int CountPages(byte[] tifBytes)
    {
        using var ms = new MemoryStream(tifBytes);
        using var collection = new MagickImageCollection();
        collection.Read(ms, MagickFormat.Tiff);
        return collection.Count;
    }
}
