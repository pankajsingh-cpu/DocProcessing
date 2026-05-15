using Microsoft.Extensions.VectorData;

namespace DocProcessing.Embeddings;

// Vector store record for an intent. Both the KB worker and the Classifier
// worker register their own in-process vector store via AddIntentKnowledgeBase,
// so this type must be public for cross-project DI.
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
    public string[] RequiredFields { get; set; } = Array.Empty<string>();

    [VectorStoreData]
    public string[] OptionalFields { get; set; } = Array.Empty<string>();

    [VectorStoreVector(Dimensions: EmbeddingDimensions)]
    public ReadOnlyMemory<float> Vector { get; set; }
}
