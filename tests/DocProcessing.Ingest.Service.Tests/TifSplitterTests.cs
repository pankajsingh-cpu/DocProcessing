using DocProcessing.Ingest.Service;

namespace DocProcessing.Ingest.Service.Tests;

public class TifSplitterTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task Splits_Multipage_Tif_Into_Single_Page_Tifs(int pageCount)
    {
        var splitter = new TifSplitter();
        var input = TifFixtures.CreateMultiPageTif(pageCount);

        using var ms = new MemoryStream(input);
        var pages = new List<DocumentPage>();
        await foreach (var page in splitter.SplitAsync(ms, CancellationToken.None))
        {
            pages.Add(page);
        }

        pages.Should().HaveCount(pageCount);
        pages.Select(p => p.PageNumber).Should().Equal(Enumerable.Range(1, pageCount));
        foreach (var page in pages)
        {
            page.TifBytes.Should().NotBeEmpty();
            TifFixtures.CountPages(page.TifBytes).Should().Be(1, "each yielded page is a single-page TIF");
        }
    }
}
