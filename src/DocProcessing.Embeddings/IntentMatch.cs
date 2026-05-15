using System.ComponentModel;

namespace DocProcessing.Embeddings;

[Description("A candidate intent returned by IIntentKnowledgeBase.SearchAsync.")]
public sealed record IntentMatch(
    [property: Description("Canonical intent name (e.g. 'change_of_address'). Use this exact value when calling GetSchemaAsync.")]
    string Name,
    [property: Description("Human-readable definition of the intent.")]
    string Definition,
    [property: Description("Keywords that frequently appear in documents of this intent.")]
    IReadOnlyList<string> Keywords,
    [property: Description("Similarity score in [0,1] — higher is more relevant to the query.")]
    double Score);
