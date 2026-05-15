using System.Text.Json.Serialization;

namespace DocProcessing.Embeddings;

// Shape of samples/intent-kb-seed.json entries.
public sealed record IntentSeed(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("definition")] string Definition,
    [property: JsonPropertyName("keywords")] string[] Keywords,
    [property: JsonPropertyName("required_fields")] string[] RequiredFields,
    [property: JsonPropertyName("optional_fields")] string[] OptionalFields);
