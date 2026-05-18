using Microsoft.Extensions.VectorData;

namespace DocProcessing.Embeddings;

// Vector store record for an intent. Both the KB worker and the Classifier
// worker register their own in-process vector store via AddIntentKnowledgeBase.
//
// The PayloadTemplateJson column stores the per-intent JSON shape verbatim —
// the LLM uses it as a template when producing classifier output. Stored as a
// string because the InMemoryVectorStore (and Azure AI Search) work with
// primitive types, not JsonElement.
//
// text-embedding-3-small produces 1536-dimensional vectors.
public sealed class IntentRecord
{
    public const int EmbeddingDimensions = 1536;

    [VectorStoreKey]
    public string Name { get; set; } = default!;

    [VectorStoreData]
    public string Definition { get; set; } = default!;

    [VectorStoreData]
    public string[] Keywords { get; set; } = Array.Empty<string>();

    [VectorStoreData]
    public string PayloadTemplateJson { get; set; } = "{}";

    [VectorStoreVector(Dimensions: EmbeddingDimensions)]
    public ReadOnlyMemory<float> Vector { get; set; }
}
