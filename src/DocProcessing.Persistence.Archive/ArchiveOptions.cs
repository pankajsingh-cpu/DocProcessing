namespace DocProcessing.Persistence.Archive;

public sealed class ArchiveOptions
{
    public const string SectionName = "Archive";

    public string ContainerName { get; set; } = "blobs";

    // Prefix where the Ingest service deposits documents.
    public string DocumentsPrefix { get; set; } = "documents";

    // Prefix the archive copy lives under; date partitioning is appended.
    public string ArchivePrefix { get; set; } = "archive";

    // Synchronous copy poll cadence — Azurite finishes the copy near-instantly,
    // but on real Azure Blob a 1+ GB TIF copy may run for seconds.
    public TimeSpan CopyPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan CopyTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
