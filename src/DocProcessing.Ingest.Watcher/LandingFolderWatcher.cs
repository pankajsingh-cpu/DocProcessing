using DocProcessing.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Options;

namespace DocProcessing.Ingest.Watcher;

public sealed class LandingFolderWatcher : BackgroundService
{
    private readonly IBus _bus;
    private readonly WatcherOptions _opts;
    private readonly ILogger<LandingFolderWatcher> _logger;
    private readonly HashSet<string> _extensions;
    private FileSystemWatcher? _fsw;
    private Debouncer<string>? _debouncer;

    public LandingFolderWatcher(
        IBus bus,
        IOptions<WatcherOptions> opts,
        ILogger<LandingFolderWatcher> logger)
    {
        _bus = bus;
        _opts = opts.Value;
        _logger = logger;
        _extensions = BuildExtensionSet(_opts.SupportedExtensions);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var path = Path.GetFullPath(_opts.LandingPath);
        Directory.CreateDirectory(path);

        _debouncer = new Debouncer<string>(
            TimeSpan.FromMilliseconds(_opts.DebounceMs),
            PublishBatchArrived);

        // FileSystemWatcher only takes a single glob filter; we watch all files
        // and filter by SupportedExtensions in OnEvent so we can accept both
        // TIF/TIFF and PDF without two parallel watchers.
        _fsw = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = _opts.IncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _fsw.Created += (_, e) => OnEvent(e);
        _fsw.Changed += (_, e) => OnEvent(e);
        _fsw.Renamed += (_, e) => OnEvent(e);
        _fsw.Error += (_, e) => _logger.LogError(e.GetException(), "FileSystemWatcher error");

        _logger.LogInformation(
            "Watching {Path} (extensions={Extensions}, debounce={DebounceMs}ms, recursive={Recursive})",
            path, string.Join(",", _extensions), _opts.DebounceMs, _opts.IncludeSubdirectories);

        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private void OnEvent(FileSystemEventArgs e)
    {
        if (e.ChangeType is WatcherChangeTypes.Deleted) return;
        if (!IsSupported(e.FullPath)) return;
        _debouncer!.Trigger(e.FullPath);
    }

    private bool IsSupported(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        return !string.IsNullOrEmpty(ext) && _extensions.Contains(ext);
    }

    private static HashSet<string> BuildExtensionSet(IEnumerable<string> extensions)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in extensions)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var ext = raw.Trim();
            if (!ext.StartsWith('.')) ext = "." + ext;
            set.Add(ext);
        }
        return set;
    }

    private async Task PublishBatchArrived(string fullPath, CancellationToken ct)
    {
        // Writer may still hold the file even after the debounce window — re-queue once if so.
        if (!IsFileReadable(fullPath))
        {
            _logger.LogDebug("File still locked, re-queueing: {Path}", fullPath);
            _debouncer!.Trigger(fullPath);
            return;
        }

        var evt = new BatchArrivedEvent(
            BatchId: Path.GetFileName(fullPath),
            SourcePath: fullPath,
            ArrivedAt: DateTimeOffset.UtcNow);

        try
        {
            await _bus.Publish(evt, ct);
            _logger.LogInformation(
                "Published BatchArrivedEvent BatchId={BatchId} Path={Path}",
                evt.BatchId, fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish BatchArrivedEvent for {Path}", fullPath);
        }
    }

    private static bool IsFileReadable(string path)
    {
        try
        {
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public override void Dispose()
    {
        _fsw?.Dispose();
        _debouncer?.Dispose();
        base.Dispose();
    }
}
