using System.ComponentModel;

namespace DocProcessing.Embeddings;

[Description("Field schema and business rules for an intent — what the classifier should extract and which conditions to flag for operator review or rejection.")]
public sealed record IntentSchema(
    [property: Description("Canonical intent name (snake_case, e.g. 'drip_ocp').")]
    string Name,
    [property: Description("Human-readable definition of the intent.")]
    string Definition,
    [property: Description(
        "Snake_case field names the classifier MUST attempt to extract. Each becomes a key in the emitted payload, " +
        "mapped to the extracted value or null when not found in the OCR text.")]
    IReadOnlyList<string> RequiredFields,
    [property: Description(
        "Snake_case field names the classifier SHOULD attempt to extract when present. Same emit rule as required_fields.")]
    IReadOnlyList<string> OptionalFields,
    [property: Description(
        "Business rules from the Desktop Operating Procedure. For each rule whose condition is satisfied by the document, " +
        "the classifier must append an entry to payload.alerts[] containing {condition, action, reason}. " +
        "Action 'reject' means the document is not processable; 'review' means human-in-the-loop validation is required.")]
    IReadOnlyList<RejectRule> RejectRules);
