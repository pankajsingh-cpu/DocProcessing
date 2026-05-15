using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;

namespace DocProcessing.Embeddings;

// Reads the intent seed file at startup, generates an embedding per intent,
// and upserts it into the "intents" collection. Embeddings are computed once
// per process start — a handful of intents fits comfortably inside GitHub
// Models' embedding rate limits.
public sealed class IntentKnowledgeBaseSeeder(
    VectorStoreCollection<string, IntentRecord> collection,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IOptions<KnowledgeBaseOptions> opts,
    ILogger<IntentKnowledgeBaseSeeder> logger) : IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly KnowledgeBaseOptions _opts = opts.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await collection.EnsureCollectionExistsAsync(cancellationToken);

        var seedPath = Path.GetFullPath(_opts.SeedPath);
        if (!File.Exists(seedPath))
        {
            logger.LogError("Intent seed file not found at {Path} — KB will start empty.", seedPath);
            return;
        }

        await using var stream = File.OpenRead(seedPath);
        var seeds = await JsonSerializer.DeserializeAsync<IntentSeed[]>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"Seed file at {seedPath} did not deserialise to IntentSeed[].");

        logger.LogInformation("Seeding {Count} intents from {Path}", seeds.Length, seedPath);

        foreach (var seed in seeds)
        {
            // Embed (definition + keywords) so queries on either match.
            var text = $"{seed.Definition}\n{string.Join(' ', seed.Keywords)}";
            var embedding = await embeddings.GenerateAsync(text, cancellationToken: cancellationToken);

            var record = new IntentRecord
            {
                Name = seed.Name,
                Definition = seed.Definition,
                Keywords = seed.Keywords,
                RequiredFields = seed.RequiredFields,
                OptionalFields = seed.OptionalFields,
                Vector = embedding.Vector,
            };

            await collection.UpsertAsync(record, cancellationToken);
            logger.LogDebug("Upserted intent {Name}", seed.Name);
        }

        logger.LogInformation("Intent KB seeded: {Count} intents.", seeds.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
