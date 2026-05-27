using Microsoft.Extensions.VectorData;

namespace DocProcessing.Embeddings;

// Vector store record for an intent. Both the KB worker and the Classifier
// worker register their own in-process vector store via AddIntentKnowledgeBase.
//
// RequiredFields / OptionalFields are the snake_case field names the classifier
// should attempt to extract — they become keys in the emitted payload.
// RejectRulesJson stores the rule list verbatim as a JSON string because the
// vector store works with primitive types, not complex object arrays.
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

    [VectorStoreData]
    public string RejectRulesJson { get; set; } = "[]";

    [VectorStoreVector(Dimensions: EmbeddingDimensions)]
    public ReadOnlyMemory<float> Vector { get; set; }
}
