using System.Text.Json.Serialization;

namespace DocProcessing.Embeddings;

// Shape of one samples/kb/*.seed.json file. Each file is the knowledge base for
// a single intent, authored from that intent's Desktop Operating Procedure (DOP).
// The classifier builds its payload from RequiredFields + OptionalFields
// (snake_case keys → extracted value or null) and consults RejectRules to flag
// documents that fail operator-side validity checks.
public sealed record IntentSeed(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("definition")] string Definition,
    [property: JsonPropertyName("keywords")] string[] Keywords,
    [property: JsonPropertyName("required_fields")] string[] RequiredFields,
    [property: JsonPropertyName("optional_fields")] string[] OptionalFields,
    [property: JsonPropertyName("reject_rules")] RejectRule[] RejectRules,
    [property: JsonPropertyName("_provenance")] IntentProvenance? Provenance);

public sealed record RejectRule(
    [property: JsonPropertyName("condition")] string Condition,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record IntentProvenance(
    [property: JsonPropertyName("source_document")] string? SourceDocument,
    [property: JsonPropertyName("source_type")] string? SourceType,
    [property: JsonPropertyName("owning_team")] string? OwningTeam,
    [property: JsonPropertyName("region")] string? Region,
    [property: JsonPropertyName("notes")] string? Notes);
