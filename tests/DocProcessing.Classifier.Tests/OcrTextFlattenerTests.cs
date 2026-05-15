namespace DocProcessing.Classifier.Tests;

public sealed class OcrTextFlattenerTests
{
    [Fact]
    public void Flatten_Concatenates_Page_Level_Content()
    {
        var json = BinaryData.FromString("""
            {
              "pages": [
                { "pageNumber": 1, "content": "First page text" },
                { "pageNumber": 2, "content": "Second page text" }
              ]
            }
            """);

        var text = OcrTextFlattener.Flatten(json);

        text.Should().Contain("PAGE 1").And.Contain("First page text");
        text.Should().Contain("PAGE 2").And.Contain("Second page text");
    }

    [Fact]
    public void Flatten_Falls_Back_To_Line_Level_Content_When_Page_Content_Absent()
    {
        var json = BinaryData.FromString("""
            {
              "pages": [
                {
                  "pageNumber": 1,
                  "lines": [
                    { "content": "Holder ID: 123" },
                    { "content": "Date: 2026-05-14" }
                  ]
                }
              ]
            }
            """);

        var text = OcrTextFlattener.Flatten(json);

        text.Should().Contain("Holder ID: 123");
        text.Should().Contain("Date: 2026-05-14");
    }

    [Fact]
    public void Flatten_Returns_Empty_When_Pages_Missing()
    {
        var json = BinaryData.FromString("""{"modelId":"prebuilt-layout"}""");

        var text = OcrTextFlattener.Flatten(json);

        text.Should().BeEmpty();
    }
}
