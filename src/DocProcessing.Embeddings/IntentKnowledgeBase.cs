using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace DocProcessing.Embeddings;

public sealed class IntentKnowledgeBase(
    VectorStoreCollection<string, IntentRecord> collection,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILogger<IntentKnowledgeBase> logger) : IIntentKnowledgeBase
{
    public async Task<IReadOnlyList<IntentMatch>> SearchAsync(
        string query,
        int top = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<IntentMatch>();
        }

        var embedding = await embeddings.GenerateAsync(query, cancellationToken: cancellationToken);

        var results = new List<IntentMatch>(capacity: top);
        await foreach (var hit in collection.SearchAsync(embedding.Vector, top, cancellationToken: cancellationToken))
        {
            var record = hit.Record;
            results.Add(new IntentMatch(
                Name: record.Name,
                Definition: record.Definition,
                Keywords: record.Keywords,
                Score: hit.Score ?? 0d));
        }

        logger.LogDebug("KB search returned {Count} intents for query of length {Length}",
            results.Count, query.Length);
        return results;
    }

    public async Task<IntentSchema?> GetSchemaAsync(
        string intentName,
        CancellationToken cancellationToken = default)
    {
        var record = await collection.GetAsync(intentName, cancellationToken: cancellationToken);
        if (record is null)
        {
            logger.LogWarning("Schema requested for unknown intent {IntentName}", intentName);
            return null;
        }

        RejectRule[] rules;
        try
        {
            rules = JsonSerializer.Deserialize<RejectRule[]>(record.RejectRulesJson) ?? Array.Empty<RejectRule>();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex,
                "Stored reject_rules for intent {IntentName} is not valid JSON; returning empty rule set",
                intentName);
            rules = Array.Empty<RejectRule>();
        }

        return new IntentSchema(
            Name: record.Name,
            Definition: record.Definition,
            RequiredFields: record.RequiredFields,
            OptionalFields: record.OptionalFields,
            RejectRules: rules);
    }
}
