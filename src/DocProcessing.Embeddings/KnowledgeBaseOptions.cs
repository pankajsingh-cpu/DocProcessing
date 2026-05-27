namespace DocProcessing.Embeddings;

public sealed class KnowledgeBaseOptions
{
    public const string SectionName = "KnowledgeBase";

    // Directory path resolved against the worker's CWD (which Aspire sets to
    // the project directory). The seeder loads every *.seed.json file in it.
    // Default points at the repo-root samples/kb folder.
    public string SeedPath { get; set; } = "../../samples/kb";

    public string CollectionName { get; set; } = "intents";
}
