using System.Text.Json.Serialization;

namespace DocProcessing.Classifier;

// Shape the agent is instructed to return. Deserialised with
// DocProcessing.Contracts.JsonOpts.StrictCamelCase, which maps these properties
// to camelCase JSON keys ("intents", "intent", "extractedFields"). Each
// ClassifierIntent maps 1:1 to a Contracts.Events.IntentResult.
public sealed record ClassifierOutput(
    IReadOnlyList<ClassifierIntent> Intents);

public sealed record ClassifierIntent(
    string Intent,
    int[] Pages,
    double Confidence,
    [property: JsonPropertyName("extractedFields")] IDictionary<string, string?> ExtractedFields);
