using System.Text.Json;
using System.Text.Json.Serialization;
using DocProcessing.Classifier;
using DocProcessing.Common.Llm;
using DocProcessing.Embeddings;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace DocProcessing.Classifier.Tests;

// Eval harness that runs the real MAF agent against samples/classifier-eval/*.json
// fixtures and reports precision/recall per intent. Skipped automatically when
// GITHUB_TOKEN is not set so CI without LLM secrets can still pass.
//
// Expand the fixture set over time; the build guide §12 calls for 50–100
// fixtures (one per known intent + edge cases).
public sealed class ClassifierEvalTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Eval_Against_Fixtures_Tracks_Precision_Recall()
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        Skip.If(string.IsNullOrWhiteSpace(token), "GITHUB_TOKEN env var not set — skipping live LLM eval.");

        var repoRoot = ResolveRepoRoot();
        var fixturesDir = Path.Combine(repoRoot, "samples", "classifier-eval");
        var seedPath = Path.Combine(repoRoot, "samples", "intent-kb-seed.json");

        var fixtures = await LoadFixtures(fixturesDir);
        fixtures.Should().NotBeEmpty("eval requires at least one fixture under samples/classifier-eval/");

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = token,
                ["Llm:Provider"] = LlmServiceCollectionExtensions.GithubModelsProvider,
                ["KnowledgeBase:SeedPath"] = seedPath,
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddIntentKnowledgeBase(config);
        services.AddDocProcChatClient(config);
        services.AddSingleton<AIAgent>(sp =>
        {
            var chat = sp.GetRequiredService<IChatClient>();
            var kb = sp.GetRequiredService<IIntentKnowledgeBase>();
            return ClassifierAgentFactory.Create(chat, kb);
        });
        services.AddOptions<ClassifierOptions>();
        services.AddSingleton<IIntentClassifier, IntentClassifier>();

        await using var sp = services.BuildServiceProvider();

        // Manually drive the KB seeder — we're not running a Host here.
        foreach (var seeder in sp.GetServices<IHostedService>().OfType<IntentKnowledgeBaseSeeder>())
        {
            await seeder.StartAsync(default);
        }

        var classifier = sp.GetRequiredService<IIntentClassifier>();
        var counts = new Dictionary<string, IntentCounts>();
        var failed = 0;

        foreach (var fix in fixtures)
        {
            output.WriteLine($"-- {fix.Name} --");
            HashSet<string> predicted;
            try
            {
                var result = await classifier.ClassifyAsync(fix.OcrText);
                predicted = result.Intents.Select(i => i.Intent).ToHashSet();
                output.WriteLine($"  predicted: [{string.Join(", ", predicted)}]");
                output.WriteLine($"  expected:  [{string.Join(", ", fix.ExpectedIntents)}]");
            }
            catch (Exception ex)
            {
                output.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
                failed++;
                continue;
            }

            var expected = fix.ExpectedIntents.ToHashSet();
            foreach (var intent in predicted.Union(expected))
            {
                var c = counts.GetValueOrDefault(intent, new IntentCounts());
                if (predicted.Contains(intent) && expected.Contains(intent)) c.TruePositive++;
                else if (predicted.Contains(intent) && !expected.Contains(intent)) c.FalsePositive++;
                else if (!predicted.Contains(intent) && expected.Contains(intent)) c.FalseNegative++;
                counts[intent] = c;
            }
        }

        output.WriteLine("");
        output.WriteLine("== Per-intent precision / recall ==");
        foreach (var (intent, c) in counts.OrderBy(kv => kv.Key))
        {
            var precision = c.TruePositive + c.FalsePositive == 0
                ? 0d : (double)c.TruePositive / (c.TruePositive + c.FalsePositive);
            var recall = c.TruePositive + c.FalseNegative == 0
                ? 0d : (double)c.TruePositive / (c.TruePositive + c.FalseNegative);
            output.WriteLine(
                $"  {intent,-22}  TP={c.TruePositive,-3} FP={c.FalsePositive,-3} FN={c.FalseNegative,-3}  P={precision:P0,4}  R={recall:P0,4}");
        }

        output.WriteLine("");
        output.WriteLine($"Fixtures total: {fixtures.Count}, failed: {failed}");

        // Don't fail the test on imperfect precision/recall — this is a tracking
        // harness, not a gate. The build guide §12 says to "track precision/recall
        // per intent over time" — turn this into a threshold gate once the eval
        // set is meaningful (50–100 fixtures).
        failed.Should().BeLessThan(fixtures.Count, "every fixture failed — wiring is broken, not just metrics.");
    }

    private static string ResolveRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "DocProcessing.slnx")))
        {
            var parent = Directory.GetParent(dir)
                ?? throw new InvalidOperationException(
                    $"Could not locate DocProcessing.slnx walking up from {AppContext.BaseDirectory}.");
            dir = parent.FullName;
        }
        return dir;
    }

    private static async Task<IReadOnlyList<EvalFixture>> LoadFixtures(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return Array.Empty<EvalFixture>();
        }

        var fixtures = new List<EvalFixture>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").OrderBy(p => p))
        {
            await using var stream = File.OpenRead(path);
            var fix = await JsonSerializer.DeserializeAsync<EvalFixture>(stream, FixtureJsonOptions);
            if (fix is not null)
            {
                fixtures.Add(fix);
            }
        }
        return fixtures;
    }

    private static readonly JsonSerializerOptions FixtureJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record EvalFixture(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("ocrText")] string OcrText,
        [property: JsonPropertyName("expectedIntents")] string[] ExpectedIntents);

    private sealed class IntentCounts
    {
        public int TruePositive { get; set; }
        public int FalsePositive { get; set; }
        public int FalseNegative { get; set; }
    }
}
