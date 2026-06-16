using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using DocProcessing.Classifier;
using DocProcessing.Common.Llm;
using DocProcessing.Embeddings;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using OpenAI.Chat;
using Xunit.Abstractions;

namespace DocProcessing.Classifier.Tests;

// End-to-end (minus messaging+SQL) sanity check that exercises real
// Azure Document Intelligence -> OcrTextFlattener -> IIntentClassifier
// against the production-shaped PDFs in samples/SampleForms.
//
// Each folder name is the ground-truth intent (DripOCP -> drip_ocp,
// Sell -> sell_stock). The test reports per-file predicted intents +
// confidence, flags any file with no recognised intent as "unknown" for
// human-in-the-loop routing, and emits a precision/recall summary per
// intent at the end. It is a tracking harness, not a hard gate — it
// only fails if classification is broken across the board.
//
// OCR JSON is cached under bin/.../ocr-cache so reruns don't incur
// further Document Intelligence cost. Delete the cache folder to force
// a re-OCR.
public sealed class SampleFormsClassificationTests(ITestOutputHelper output)
{
    private const string DripOcpIntent = "drip_ocp";
    private const string SellStockIntent = "sell_stock";

    [SkippableFact]
    public async Task Classifies_SampleForms_DripOcp_And_Sell()
    {
        // Credentials come from (in order): user-secrets on this test project →
        // process env vars. Set once via:
        //   dotnet user-secrets set "GITHUB_TOKEN" "ghp_..." --project tests/DocProcessing.Classifier.Tests
        //   dotnet user-secrets set "AzureAI:DocumentIntelligence:Endpoint" "https://..." --project tests/DocProcessing.Classifier.Tests
        //   dotnet user-secrets set "AzureAI:DocumentIntelligence:Key" "..." --project tests/DocProcessing.Classifier.Tests
        // Env-var path remains supported for CI and ad-hoc shells.
        var credConfig = new ConfigurationBuilder()
            .AddUserSecrets<SampleFormsClassificationTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var token = credConfig["GITHUB_TOKEN"];
        Skip.If(string.IsNullOrWhiteSpace(token),
            "GITHUB_TOKEN not configured — set via `dotnet user-secrets set GITHUB_TOKEN ghp_... " +
            "--project tests/DocProcessing.Classifier.Tests` or export the env var.");

        var diEndpoint = credConfig["AzureAI:DocumentIntelligence:Endpoint"];
        var diKey = credConfig["AzureAI:DocumentIntelligence:Key"];
        Skip.If(string.IsNullOrWhiteSpace(diEndpoint) || string.IsNullOrWhiteSpace(diKey),
            "AzureAI:DocumentIntelligence:Endpoint / :Key not configured — " +
            "set via `dotnet user-secrets set` on tests/DocProcessing.Classifier.Tests or export the env vars.");

        var repoRoot = ResolveRepoRoot();
        var seedPath = Path.Combine(repoRoot, "samples", "kb");
        var sampleFormsRoot = Path.Combine(repoRoot, "samples", "SampleForms");
        var ocrCacheRoot = Path.Combine(AppContext.BaseDirectory, "ocr-cache");
        Directory.CreateDirectory(ocrCacheRoot);

        var groups = new[]
        {
            new SampleGroup("DripOCP", DripOcpIntent),
            new SampleGroup("Sell",    SellStockIntent),
        };

        var pdfs = LoadPdfs(sampleFormsRoot, groups);
        pdfs.Should().NotBeEmpty("samples/SampleForms/{DripOCP,Sell} must contain at least one PDF.");

        var diClient = new DocumentIntelligenceClient(new Uri(diEndpoint!), new AzureKeyCredential(diKey!));

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = token,
                ["Llm:Provider"] = LlmServiceCollectionExtensions.GithubModelsProvider,
                ["KnowledgeBase:SeedPath"] = seedPath,
                // Single-shot mode: surface 429s immediately instead of Polly-sleeping
                // up to 3× Retry-After per file. A rate-limited file is counted as
                // Failed and the run keeps moving.
                ["Classifier:RateLimitMaxRetries"] = "0",
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddIntentKnowledgeBase(config);

        // Direct IChatClient registration (skipping AddDocProcChatClient) so we
        // can override two things that the production wiring doesn't expose:
        //   1. RetryPolicy = no retries — the OpenAI SDK retries 429s up to 3
        //      times by default with multi-second backoffs, which makes a
        //      rate-limited file take minutes per attempt. For an eval/test
        //      harness we want immediate failure on 429 so the run moves on.
        //   2. Model = openai/gpt-4o-mini — the build guide §6 calls this out
        //      as the higher-quota fallback when openai/gpt-4.1's 50/day cap
        //      is exhausted. Functionally equivalent for KB-grounded intent
        //      classification on these forms.
        services.AddSingleton<IChatClient>(_ =>
        {
            var openAiOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(LlmServiceCollectionExtensions.DefaultEndpoint),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            };
            return new ChatClient("openai/gpt-4o-mini", new ApiKeyCredential(token!), openAiOptions)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();
        });

        services.AddSingleton<AIAgent>(sp =>
        {
            var chat = sp.GetRequiredService<IChatClient>();
            var kb = sp.GetRequiredService<IIntentKnowledgeBase>();
            return ClassifierAgentFactory.Create(chat, kb);
        });
        services.AddOptions<ClassifierOptions>()
            .Bind(config.GetSection(ClassifierOptions.SectionName));
        services.AddSingleton<IIntentClassifier, IntentClassifier>();

        await using var sp = services.BuildServiceProvider();

        // KB is seeded by the IntentKnowledgeBaseSeeder IHostedService; we're
        // not running a Host here so drive it manually.
        foreach (var seeder in sp.GetServices<IHostedService>().OfType<IntentKnowledgeBaseSeeder>())
        {
            await seeder.StartAsync(default);
        }

        var classifier = sp.GetRequiredService<IIntentClassifier>();
        var results = new List<FileResult>();
        var counts = new Dictionary<string, IntentCounts>();
        var unknownForHitl = 0;
        var failed = 0;

        // Inter-file pacing: GitHub Models free tier is ~10 RPM and the MAF
        // tool loop makes ~3 LLM calls per file, so an unpaced run trips 429
        // after ~4 files. Sleeping 15s between files keeps us at ~2.4 files/min
        // = ~7 LLM calls/min, comfortably under the cap. Knob is the
        // SAMPLEFORMS_PACE_MS env var so future runs can tune without a recompile.
        var paceMs = int.TryParse(Environment.GetEnvironmentVariable("SAMPLEFORMS_PACE_MS"), out var p) ? p : 15_000;
        Emit($"[START] {pdfs.Count} files queued ({pdfs.Count(p => p.Group == "DripOCP")} DripOCP + {pdfs.Count(p => p.Group == "Sell")} Sell). Retries off. Pace={paceMs}ms between files.");

        var fileIndex = 0;
        foreach (var pdf in pdfs)
        {
            fileIndex++;
            if (fileIndex > 1 && paceMs > 0)
            {
                Emit($"  .. pacing {paceMs}ms before next file");
                await Task.Delay(paceMs);
            }
            Emit($"[{fileIndex,2}/{pdfs.Count}] RUN  {pdf.Group}/{pdf.FileName}  expected={pdf.ExpectedIntent}");

            string ocrJson;
            try
            {
                ocrJson = await OcrWithCacheAsync(diClient, pdf, ocrCacheRoot);
            }
            catch (Exception ex)
            {
                Emit($"  -> OCR FAILED: {ex.GetType().Name}: {Squash(ex.Message)}");
                failed++;
                EmitProgress(fileIndex, pdfs.Count, results, unknownForHitl, failed);
                continue;
            }

            var text = OcrTextFlattener.Flatten(BinaryData.FromString(ocrJson));
            if (string.IsNullOrWhiteSpace(text))
            {
                Emit("  -> OCR produced no text — skipping classify.");
                failed++;
                EmitProgress(fileIndex, pdfs.Count, results, unknownForHitl, failed);
                continue;
            }

            ClassifierOutput classification;
            var sw = Stopwatch.StartNew();
            try
            {
                classification = await classifier.ClassifyAsync(text);
            }
            catch (Exception ex)
            {
                Emit($"  -> CLASSIFY FAILED: {ex.GetType().Name}: {Squash(ex.Message)}");
                failed++;
                EmitProgress(fileIndex, pdfs.Count, results, unknownForHitl, failed);
                continue;
            }
            sw.Stop();

            var predicted = classification.Intents
                .Select(i => new PredictedIntent(i.Intent, i.Confidence))
                .ToList();

            // HITL filter: if the classifier returned no intents at all, or only
            // intents we are not yet trained to recognise (anything other than
            // drip_ocp / sell_stock), treat as unknown.
            var recognised = predicted
                .Where(p => p.Intent is DripOcpIntent or SellStockIntent)
                .ToList();

            var status = recognised.Count == 0 ? "unknown -> HITL" :
                recognised.Any(p => p.Intent == pdf.ExpectedIntent) ? "match" : "mismatch";

            if (recognised.Count == 0)
            {
                unknownForHitl++;
                Emit($"  -> predicted: (none recognised) raw=[{Format(predicted)}]  status=unknown->HITL  llm={sw.ElapsedMilliseconds}ms");
            }
            else
            {
                Emit($"  -> predicted: [{Format(recognised)}]  status={status}  llm={sw.ElapsedMilliseconds}ms");
            }

            results.Add(new FileResult(pdf.Group, pdf.FileName, pdf.ExpectedIntent, recognised, status));

            // Update precision/recall counts. We only score the two intents the
            // classifier is trained on today; any other returned intent is
            // ignored for metrics (the HITL filter already accounts for it).
            var predictedSet = recognised.Select(p => p.Intent).ToHashSet();
            var expectedSet = new HashSet<string> { pdf.ExpectedIntent };
            foreach (var intent in predictedSet.Union(expectedSet))
            {
                var c = counts.GetValueOrDefault(intent, new IntentCounts());
                if (predictedSet.Contains(intent) && expectedSet.Contains(intent)) c.TruePositive++;
                else if (predictedSet.Contains(intent) && !expectedSet.Contains(intent)) c.FalsePositive++;
                else if (!predictedSet.Contains(intent) && expectedSet.Contains(intent)) c.FalseNegative++;
                counts[intent] = c;
            }

            EmitProgress(fileIndex, pdfs.Count, results, unknownForHitl, failed);
        }

        Emit("");
        Emit("== Per-intent precision / recall ==");
        foreach (var (intent, c) in counts.OrderBy(kv => kv.Key))
        {
            var precision = c.TruePositive + c.FalsePositive == 0
                ? 0d : (double)c.TruePositive / (c.TruePositive + c.FalsePositive);
            var recall = c.TruePositive + c.FalseNegative == 0
                ? 0d : (double)c.TruePositive / (c.TruePositive + c.FalseNegative);
            Emit(
                $"  {intent,-12}  TP={c.TruePositive,-3} FP={c.FalsePositive,-3} FN={c.FalseNegative,-3}  P={precision:P0}  R={recall:P0}");
        }

        var matched = results.Count(r => r.Status == "match");
        Emit("");
        Emit($"Files total:    {pdfs.Count}");
        Emit($"Matched:        {matched}");
        Emit($"Mismatched:     {results.Count(r => r.Status == "mismatch")}");
        Emit($"Unknown (HITL): {unknownForHitl}");
        Emit($"Failed:         {failed}");

        // Mirror summary into the xUnit ITestOutputHelper too — handy when reading
        // the test result in a Test Explorer or trx file rather than the console.
        output.WriteLine($"Done. matched={matched} mismatched={results.Count(r => r.Status == "mismatch")} hitl={unknownForHitl} failed={failed}");

        // Tracking harness — only fail when wiring is broken (zero successes).
        failed.Should().BeLessThan(pdfs.Count,
            "every file failed — wiring (DI / classifier / token) is broken, not just metrics.");
        (matched + unknownForHitl + results.Count(r => r.Status == "mismatch"))
            .Should().BeGreaterThan(0, "no document made it through the pipeline.");
    }

    // xUnit captures both ITestOutputHelper writes and stdout (Console.WriteLine)
    // and only emits them after the test finishes, so progress would be invisible
    // for a multi-minute run. Sidestep the capture by appending to a flat UTF-8
    // file the user (or another shell) can `tail -f`. The file lives next to the
    // test binaries so it's easy to find: bin/.../sample-forms-progress.log.
    private static readonly object EmitLock = new();
    private static readonly string ProgressLogPath = ResolveProgressLogPath();

    private static string ResolveProgressLogPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "sample-forms-progress.log");
        try
        {
            File.WriteAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === run begin ===\n");
        }
        catch
        {
            // If the file can't be truncated (e.g. another tail holds it), just
            // append to whatever's there.
        }
        return path;
    }

    private static void Emit(string line)
    {
        var stamped = string.IsNullOrEmpty(line)
            ? Environment.NewLine
            : $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
        lock (EmitLock)
        {
            try
            {
                File.AppendAllText(ProgressLogPath, stamped);
            }
            catch
            {
                // Progress log is best-effort — never let it break the test.
            }
        }
        // Best-effort to ITestOutputHelper too, so the trx / Test Explorer view
        // still has the trail when the test finishes.
        Console.WriteLine(stamped.TrimEnd());
    }

    private static void EmitProgress(int index, int total, List<FileResult> results, int hitl, int failed)
    {
        var matched = results.Count(r => r.Status == "match");
        var mismatched = results.Count(r => r.Status == "mismatch");
        Emit($"  .. progress {index}/{total}  matched={matched}  mismatched={mismatched}  hitl={hitl}  failed={failed}");
    }

    private static string Squash(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var oneLine = s.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length > 200 ? oneLine[..200] + "..." : oneLine;
    }

    private static async Task<string> OcrWithCacheAsync(
        DocumentIntelligenceClient client, PdfSample pdf, string cacheRoot)
    {
        var cacheKey = $"{pdf.Group}__{Path.GetFileNameWithoutExtension(pdf.FullPath)}.json";
        var cachePath = Path.Combine(cacheRoot, cacheKey);
        if (File.Exists(cachePath))
        {
            return await File.ReadAllTextAsync(cachePath);
        }

        var bytes = await File.ReadAllBytesAsync(pdf.FullPath);
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed, "prebuilt-layout", BinaryData.FromBytes(bytes));
        var json = BinaryData.FromObjectAsJson(operation.Value).ToString();
        await File.WriteAllTextAsync(cachePath, json);
        return json;
    }

    private static IReadOnlyList<PdfSample> LoadPdfs(string root, IEnumerable<SampleGroup> groups)
    {
        // Optional cap for demo runs — process only the first N files per folder.
        // Set SAMPLEFORMS_MAX_FILES_PER_FOLDER=2 for a ~80s demo over 4 documents
        // instead of the full 40-file pass.
        var maxPerFolder = int.TryParse(Environment.GetEnvironmentVariable("SAMPLEFORMS_MAX_FILES_PER_FOLDER"), out var m) && m > 0
            ? m
            : int.MaxValue;

        var list = new List<PdfSample>();
        foreach (var g in groups)
        {
            var dir = Path.Combine(root, g.FolderName);
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "*.pdf").OrderBy(p => p).Take(maxPerFolder))
            {
                list.Add(new PdfSample(g.FolderName, g.ExpectedIntent, Path.GetFileName(path), path));
            }
        }
        return list;
    }

    private static string Format(IEnumerable<PredictedIntent> intents) =>
        string.Join(", ", intents.Select(p => $"{p.Intent}@{p.Confidence:P0}"));

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

    private sealed record SampleGroup(string FolderName, string ExpectedIntent);

    private sealed record PdfSample(string Group, string ExpectedIntent, string FileName, string FullPath);

    private sealed record PredictedIntent(string Intent, double Confidence);

    private sealed record FileResult(
        string Group, string FileName, string ExpectedIntent,
        IReadOnlyList<PredictedIntent> Predicted, string Status);

    private sealed class IntentCounts
    {
        public int TruePositive { get; set; }
        public int FalsePositive { get; set; }
        public int FalseNegative { get; set; }
    }
}
