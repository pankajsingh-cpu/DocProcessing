namespace DocProcessing.Ingest.Service;

public sealed class IngestOptions
{
    public const string SectionName = "Ingest";

    public string ContainerName { get; set; } = "blobs";
    public string DocumentPrefix { get; set; } = "documents";

    // Defensive cross-check on the file extension the watcher passes in. Belt
    // and braces — the watcher already filters, but we don't trust an event
    // arriving via DLQ replay or a manual publish to be well-formed.
    public string[] SupportedExtensions { get; set; } = [".tif", ".tiff", ".pdf"];
}
