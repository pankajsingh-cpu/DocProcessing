using System.Text.Json;
using DocProcessing.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocProcessing.Ocr.Tests;

public class StubOcrEngineTests : IAsyncLifetime
{
    private string _fixtureDir = string.Empty;

    public Task InitializeAsync()
    {
        _fixtureDir = Path.Combine(Path.GetTempPath(), "dp-ocr-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixtureDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(_fixtureDir, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    private StubOcrEngine NewEngine() => new(
        Options.Create(new OcrOptions { FixturePath = _fixtureDir, GenericFixtureName = "_generic.json" }),
        NullLogger<StubOcrEngine>.Instance);

    [Fact]
    public async Task Loads_Specific_Fixture_When_DocumentId_Matches_File()
    {
        var docId = Guid.NewGuid();
        var specific = new
        {
            modelId = "prebuilt-layout",
            pages = new[]
            {
                new { pageNumber = 1, content = "Page 1 content" },
                new { pageNumber = 2, content = "Page 2 content" },
            },
        };
        await File.WriteAllBytesAsync(
            Path.Combine(_fixtureDir, $"{docId}.json"),
            JsonSerializer.SerializeToUtf8Bytes(specific));

        var engine = NewEngine();
        var result = await engine.AnalyseAsync(docId, [], CancellationToken.None);

        result.PageCount.Should().Be(2);
        var json = JsonSerializer.Deserialize<JsonElement>(result.JsonPayload);
        json.GetProperty("modelId").GetString().Should().Be("prebuilt-layout");
    }

    [Fact]
    public async Task Falls_Back_To_Generic_Fixture_When_No_Specific_File()
    {
        var generic = new { modelId = "generic-stub", pages = new[] { new { pageNumber = 1, content = "Generic" } } };
        await File.WriteAllBytesAsync(
            Path.Combine(_fixtureDir, "_generic.json"),
            JsonSerializer.SerializeToUtf8Bytes(generic));

        var engine = NewEngine();
        var result = await engine.AnalyseAsync(Guid.NewGuid(), [], CancellationToken.None);

        result.PageCount.Should().Be(1);
        JsonSerializer.Deserialize<JsonElement>(result.JsonPayload)
            .GetProperty("modelId").GetString().Should().Be("generic-stub");
    }

    [Fact]
    public async Task Falls_Back_To_Inline_Fixture_When_Neither_File_Exists()
    {
        // Empty fixture dir → engine uses its built-in 1-page payload.
        var engine = NewEngine();
        var result = await engine.AnalyseAsync(Guid.NewGuid(), [], CancellationToken.None);

        result.PageCount.Should().Be(1);
        result.JsonPayload.Should().NotBeEmpty();
        var json = JsonSerializer.Deserialize<JsonElement>(result.JsonPayload);
        json.GetProperty("modelId").GetString().Should().Be("stub-prebuilt-layout");
    }
}
