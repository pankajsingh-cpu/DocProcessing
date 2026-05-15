using System.ComponentModel;

namespace DocProcessing.Embeddings;

[Description("Field schema for an intent — which fields must be extracted from the document and which are optional.")]
public sealed record IntentSchema(
    [property: Description("Canonical intent name.")]
    string Name,
    [property: Description("Human-readable definition of the intent.")]
    string Definition,
    [property: Description("Fields that MUST appear in the classifier output for this intent. " +
                          "Use null if a required field cannot be found in the OCR text.")]
    IReadOnlyList<string> RequiredFields,
    [property: Description("Fields that are useful when present but are not required.")]
    IReadOnlyList<string> OptionalFields);
