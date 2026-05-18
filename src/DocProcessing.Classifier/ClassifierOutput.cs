using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocProcessing.Classifier;

// Shape the agent is instructed to return. Deserialised with
// DocProcessing.Contracts.JsonOpts.StrictCamelCase, which maps these properties
// to camelCase JSON keys ("intents", "intent", "confidence", "payload").
//
// The payload itself uses the snake_case shape defined by the KB's
// payload_template for the chosen intent — it's preserved verbatim as a
// JsonElement so downstream consumers see exactly what the LLM produced.
public sealed record ClassifierOutput(
    IReadOnlyList<ClassifierIntent> Intents);

public sealed record ClassifierIntent(
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("payload")] JsonElement Payload);
