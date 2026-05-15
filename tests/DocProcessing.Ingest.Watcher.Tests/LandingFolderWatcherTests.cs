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

    [Fact]
    public async Task Publishes_BatchArrivedEvent_When_Tif_Created()
    {
        await using var provider = new ServiceCollection()
            .AddLogging(b => b.AddDebug())
            .AddOptions<WatcherOptions>().Configure(o =>
            {
                o.LandingPath = _tempDir;
                o.DebounceMs = 150;
                o.Filter = "*.tif";
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

        var tifPath = Path.Combine(_tempDir, "batch001.tif");
        await File.WriteAllBytesAsync(tifPath, [0x49, 0x49, 0x2A, 0x00]);

        var any = await harness.Published.Any<BatchArrivedEvent>(
            ctx => ((BatchArrivedEvent)ctx.MessageObject).BatchId == "batch001.tif");

        any.Should().BeTrue();

        var batch = harness.Published.Select<BatchArrivedEvent>().First().Context.Message;
        batch.SourcePath.Should().Be(tifPath);
        batch.ArrivedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));

        await watcher.StopAsync(default);
        await harness.Stop();
    }

    [Fact]
    public async Task Ignores_Non_Matching_Extensions()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddOptions<WatcherOptions>().Configure(o =>
            {
                o.LandingPath = _tempDir;
                o.DebounceMs = 150;
                o.Filter = "*.tif";
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

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ignore.txt"), "noise");
        await Task.Delay(400);

        (await harness.Published.Any<BatchArrivedEvent>()).Should().BeFalse();

        await watcher.StopAsync(default);
        await harness.Stop();
    }
}
