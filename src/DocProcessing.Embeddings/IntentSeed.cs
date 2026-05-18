using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocProcessing.Embeddings;

// Shape of samples/intent-kb-seed.json entries. PayloadTemplate is a JSON
// object describing exactly what the classifier should extract for this
// intent — keys, nesting, types. The classifier returns the template
// filled in, with null for fields it cannot find in the OCR text.
public sealed record IntentSeed(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("definition")] string Definition,
    [property: JsonPropertyName("keywords")] string[] Keywords,
    [property: JsonPropertyName("payload_template")] JsonElement PayloadTemplate);
