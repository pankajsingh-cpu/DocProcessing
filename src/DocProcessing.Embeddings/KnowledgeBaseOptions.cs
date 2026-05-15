namespace DocProcessing.Embeddings;

public sealed class KnowledgeBaseOptions
{
    public const string SectionName = "KnowledgeBase";

    // Path resolved against the worker's CWD (which Aspire sets to the project
    // directory). Default points at the repo-root samples folder.
    public string SeedPath { get; set; } = "../../samples/intent-kb-seed.json";

    public string CollectionName { get; set; } = "intents";
}
