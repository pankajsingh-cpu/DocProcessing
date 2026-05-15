namespace DocProcessing.Ingest.Service;

public sealed class IngestOptions
{
    public const string SectionName = "Ingest";

    public string ContainerName { get; set; } = "blobs";
    public string DocumentPrefix { get; set; } = "documents";
}
