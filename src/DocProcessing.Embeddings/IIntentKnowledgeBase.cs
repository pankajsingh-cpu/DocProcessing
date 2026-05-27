using System.ComponentModel;

namespace DocProcessing.Embeddings;

// Shared abstraction so the Classifier can take a dependency on the KB
// contract without taking a project reference on the KB worker — each
// becomes a separate pod in cloud.
//
// Method signatures and [Description] attributes are the source of truth for
// the MAF tool surface; AIFunctionFactory reads them when registering
// search_intent_kb and get_intent_schema.
[Description("Knowledge base of Computershare backoffice transaction types. " +
             "Use to look up which transaction types could apply to OCR text and to fetch their field schema and business rules.")]
public interface IIntentKnowledgeBase
{
    [Description("Hybrid-search the intent knowledge base by free-text query " +
                 "(typically a snippet of OCR text). Returns up to `top` candidate intents " +
                 "ordered by relevance, each with its definition and keywords. " +
                 "ALWAYS call this BEFORE choosing an intent name — never invent intents.")]
    Task<IReadOnlyList<IntentMatch>> SearchAsync(
        [Description("Free-text query derived from the OCR text. May be a sentence, phrase, or page excerpt.")]
        string query,
        [Description("Maximum number of intent matches to return. Defaults to 3.")]
        int top = 3,
        CancellationToken cancellationToken = default);

    [Description("Returns the field schema and reject_rules for a named intent — the required and optional " +
                 "snake_case fields the classifier should extract, plus the business rules an operator would " +
                 "apply to decide whether the document is processable. " +
                 "Call AFTER SearchAsync once an intent name has been chosen. Build the payload from the field " +
                 "lists; surface any rule whose condition matches the document in payload.alerts.")]
    Task<IntentSchema?> GetSchemaAsync(
        [Description("The intent name as returned by SearchAsync. Must match exactly (case-sensitive, snake_case).")]
        string intentName,
        CancellationToken cancellationToken = default);
}
