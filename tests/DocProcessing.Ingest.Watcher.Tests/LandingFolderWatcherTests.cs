using DocProcessing.Contracts.Events;
using DocProcessing.Ingest.Watcher;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocProcessing.Ingest.Watcher.Tests;

public class LandingFolderWatcherTests : IAsyncLifetime
{
    private string _tempDir = string.Empty;

    public Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dp-watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* swallow */ }
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("batch001.tif")]
    [InlineData("batch002.tiff")]
    [InlineData("batch003.pdf")]
    public async Task Publishes_BatchArrivedEvent_For_Supported_Extension(string fileName)
    {
        await using var provider = new ServiceCollection()
            .AddLogging(b => b.AddDebug())
            .AddOptions<WatcherOptions>().Configure(o =>
            {
                o.LandingPath = _tempDir;
                o.DebounceMs = 150;
                o.SupportedExtensions = [".tif", ".tiff", ".pdf"];
                o.IncludeSubdirectories = false;
            }).Services
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var bus = provider.GetRequiredService<IBus>();
        var opts = provider.GetRequiredService<IOptions<WatcherOptions>>();
        var logger = provider.GetRequiredService<ILogger<LandingFolderWatcher>>();

        var watcher = new LandingFolderWatcher(bus, opts, logger);
        await watcher.StartAsync(default);

        // FSW needs a tick to begin raising events
        await Task.Delay(150);

        var fullPath = Path.Combine(_tempDir, fileName);
        await File.WriteAllBytesAsync(fullPath, [0x49, 0x49, 0x2A, 0x00]);

        var any = await harness.Published.Any<BatchArrivedEvent>(
            ctx => ((BatchArrivedEvent)ctx.MessageObject).BatchId == fileName);

        any.Should().BeTrue();

        var batch = harness.Published.Select<BatchArrivedEvent>().First().Context.Message;
        batch.SourcePath.Should().Be(fullPath);

        await watcher.StopAsync(default);
        await harness.Stop();
    }

    [Fact]
    public async Task Ignores_Files_Outside_SupportedExtensions()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddOptions<WatcherOptions>().Configure(o =>
            {
                o.LandingPath = _tempDir;
                o.DebounceMs = 150;
                o.SupportedExtensions = [".tif", ".tiff", ".pdf"];
                o.IncludeSubdirectories = false;
            }).Services
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var watcher = new LandingFolderWatcher(
            provider.GetRequiredService<IBus>(),
            provider.GetRequiredService<IOptions<WatcherOptions>>(),
            provider.GetRequiredService<ILogger<LandingFolderWatcher>>());

        await watcher.StartAsync(default);
        await Task.Delay(150);

        // .txt and .docx land in the folder but should be filtered out.
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ignore.txt"), "noise");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "report.docx"), "noise");
        await Task.Delay(400);

        (await harness.Published.Any<BatchArrivedEvent>()).Should().BeFalse();

        await watcher.StopAsync(default);
        await harness.Stop();
    }

    [Fact]
    public async Task Extension_Match_Is_Case_Insensitive()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddOptions<WatcherOptions>().Configure(o =>
            {
                o.LandingPath = _tempDir;
                o.DebounceMs = 150;
                o.SupportedExtensions = [".pdf"];
                o.IncludeSubdirectories = false;
            }).Services
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var watcher = new LandingFolderWatcher(
            provider.GetRequiredService<IBus>(),
            provider.GetRequiredService<IOptions<WatcherOptions>>(),
            provider.GetRequiredService<ILogger<LandingFolderWatcher>>());

        await watcher.StartAsync(default);
        await Task.Delay(150);

        var fullPath = Path.Combine(_tempDir, "MIXED-Case.PDF");
        await File.WriteAllBytesAsync(fullPath, [0x25, 0x50, 0x44, 0x46]); // %PDF

        var any = await harness.Published.Any<BatchArrivedEvent>(
            ctx => ((BatchArrivedEvent)ctx.MessageObject).BatchId == "MIXED-Case.PDF");
        any.Should().BeTrue();

        await watcher.StopAsync(default);
        await harness.Stop();
    }
}
