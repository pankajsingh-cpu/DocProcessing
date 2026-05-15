namespace DocProcessing.Ingest.Watcher;

public sealed class WatcherOptions
{
    public const string SectionName = "Watcher";

    public string LandingPath { get; set; } = string.Empty;
    public int DebounceMs { get; set; } = 500;
    public string Filter { get; set; } = "*.tif";
    public bool IncludeSubdirectories { get; set; } = true;
}
