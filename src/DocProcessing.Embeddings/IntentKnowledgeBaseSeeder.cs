using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;

namespace DocProcessing.Embeddings;

// Reads every samples/kb/*.seed.json file at startup, generates an embedding per
// intent, and upserts it into the "intents" collection. Embeddings are computed
// once per process start — a handful of intents fits comfortably inside GitHub
// Models' embedding rate limits.
//
// Each seed file is the authoritative knowledge base for one intent, authored
// from that intent's Desktop Operating Procedure (DOP). Drop a new
// *.seed.json into the directory to add an intent — no code change required.
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

        var seedDir = Path.GetFullPath(_opts.SeedPath);
        if (!Directory.Exists(seedDir))
        {
            logger.LogError("Intent seed directory not found at {Path} — KB will start empty.", seedDir);
            return;
        }

        var seedFiles = Directory.EnumerateFiles(seedDir, "*.seed.json").OrderBy(p => p).ToList();
        if (seedFiles.Count == 0)
        {
            logger.LogWarning("No *.seed.json files found in {Path} — KB will start empty.", seedDir);
            return;
        }

        logger.LogInformation("Seeding {Count} intents from {Path}", seedFiles.Count, seedDir);

        foreach (var file in seedFiles)
        {
            await using var stream = File.OpenRead(file);
            var seed = await JsonSerializer.DeserializeAsync<IntentSeed>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException($"Seed file {file} did not deserialise to IntentSeed.");

            // Embed (definition + keywords) so queries on either match.
            var text = $"{seed.Definition}\n{string.Join(' ', seed.Keywords)}";
            var embedding = await embeddings.GenerateAsync(text, cancellationToken: cancellationToken);

            var record = new IntentRecord
            {
                Name = seed.Name,
                Definition = seed.Definition,
                Keywords = seed.Keywords,
                RequiredFields = seed.RequiredFields ?? Array.Empty<string>(),
                OptionalFields = seed.OptionalFields ?? Array.Empty<string>(),
                RejectRulesJson = JsonSerializer.Serialize(seed.RejectRules ?? Array.Empty<RejectRule>()),
                Vector = embedding.Vector,
            };

            await collection.UpsertAsync(record, cancellationToken);
            logger.LogDebug("Upserted intent {Name} from {File}", seed.Name, Path.GetFileName(file));
        }

        logger.LogInformation("Intent KB seeded: {Count} intents.", seedFiles.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
