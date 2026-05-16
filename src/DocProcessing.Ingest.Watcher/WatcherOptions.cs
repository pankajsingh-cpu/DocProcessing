namespace DocProcessing.Ingest.Watcher;

public sealed class WatcherOptions
{
    public const string SectionName = "Watcher";

    public string LandingPath { get; set; } = string.Empty;
    public int DebounceMs { get; set; } = 500;

    // Extensions are matched case-insensitively. FileSystemWatcher's Filter
    // only takes one glob, so we watch *.* and filter in code.
    public string[] SupportedExtensions { get; set; } = [".tif", ".tiff", ".pdf"];

    public bool IncludeSubdirectories { get; set; } = true;
}
