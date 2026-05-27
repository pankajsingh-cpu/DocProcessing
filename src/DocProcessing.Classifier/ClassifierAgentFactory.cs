using DocProcessing.Embeddings;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DocProcessing.Classifier;

// Builds the MAF ChatClientAgent with KB tools wired. The tool method groups
// are adapted via AIFunctionFactory.Create — descriptions also live on the
// [Description] attributes on IIntentKnowledgeBase but we pass them explicitly
// so the agent's tool-calling JSON names match the system prompt.
//
// Returned as AIAgent so callers depend on the abstract base, not the concrete
// ChatClientAgent.
public static class ClassifierAgentFactory
{
    public const string AgentName = "IntentClassifier";

    public const string SearchToolName = "search_intent_kb";
    public const string SchemaToolName = "get_intent_schema";

    public static AIAgent Create(IChatClient chatClient, IIntentKnowledgeBase kb)
    {
        var searchTool = AIFunctionFactory.Create(
            (Func<string, int, CancellationToken, Task<IReadOnlyList<IntentMatch>>>)kb.SearchAsync,
            name: SearchToolName,
            description:
                "Hybrid-search the intent knowledge base by free-text query (typically a snippet of OCR text). " +
                "Returns up to `top` candidate intents ordered by relevance, each with its definition and keywords. " +
                "ALWAYS call this BEFORE choosing an intent — never invent intents.");

        var schemaTool = AIFunctionFactory.Create(
            (Func<string, CancellationToken, Task<IntentSchema?>>)kb.GetSchemaAsync,
            name: SchemaToolName,
            description:
                "Returns the field schema and reject_rules for a named intent: required_fields, optional_fields, " +
                "and the operator-side business rules. Call after search_intent_kb once an intent has been chosen, " +
                "to learn which fields to extract and which conditions to surface in payload.alerts.");

        return new ChatClientAgent(
            chatClient,
            instructions: SystemPrompts.Classifier,
            name: AgentName,
            description: "Computershare backoffice document classifier (KB-grounded, JSON-only).",
            tools: [searchTool, schemaTool]);
    }
}
