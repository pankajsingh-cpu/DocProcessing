using System.ComponentModel;
using System.Text.Json;

namespace DocProcessing.Embeddings;

[Description("Field schema for an intent — the JSON payload template the classifier should fill in for documents of this transaction type.")]
public sealed record IntentSchema(
    [property: Description("Canonical intent name (snake_case, e.g. 'drip_ocp').")]
    string Name,
    [property: Description("Human-readable definition of the intent.")]
    string Definition,
    [property: Description(
        "JSON payload template describing the exact shape of the data to extract. " +
        "Keys are snake_case. Where a value is null, the classifier should look for that field in the OCR text " +
        "and emit the value if found, or keep null if not found. Booleans should remain booleans, arrays remain arrays.")]
    JsonElement PayloadTemplate);
