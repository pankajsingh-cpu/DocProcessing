using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Globalization;
using System.Text;
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
using OpenAI.Embeddings;
using Xunit.Abstractions;

namespace DocProcessing.Classifier.Tests;

// Token-profiling harness for DripOCP documents.
//
// Unlike SampleFormsClassificationTests (which tracks precision/recall), this
// test instruments every *external foundry resource* the pipeline touches and
// records, per document, the token / page / char cost of each:
//
//   - OCR  (Azure Document Intelligence prebuilt-layout): pages + flattened
//          char count. DI is billed per page, not per token, so there is no
//          token figure to report — pages/chars are the real cost units.
//   - Embeddings (text-embedding-3-small, one call per search_intent_kb tool
//          invocation): input tokens, summed across the agent's tool loop.
//   - Chat LLM (the model under test, MAF agent loop): input + output tokens
//          (output already includes any reasoning tokens), summed across every
//          round-trip in the tool-calling loop.
//
// The chat model is selected via the PROFILE_CHAT_MODEL env var so the same
// harness can be run once per model (gpt-4.1, gpt-5, ...) and the CSVs diffed.
// Doc count is capped by PROFILE_MAX_DOCS (default 5) to respect the GitHub
// Models free-tier daily quota.
//
// Output: <repoRoot>/dripocp-token-profile-<model>.csv plus a live progress log
// next to the test binaries.
public sealed class DripOcpTokenProfilingTests(ITestOutputHelper output)
{
    private const string DripOcpIntent = "drip_ocp";

    [SkippableFact]
    public async Task Profiles_DripOcp_TokenUsage()
    {
        var credConfig = new ConfigurationBuilder()
            .AddUserSecrets<DripOcpTokenProfilingTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var token = credConfig["GITHUB_TOKEN"];
        Skip.If(string.IsNullOrWhiteSpace(token), "GITHUB_TOKEN not configured.");

        var diEndpoint = credConfig["AzureAI:DocumentIntelligence:Endpoint"];
        var diKey = credConfig["AzureAI:DocumentIntelligence:Key"];
        Skip.If(string.IsNullOrWhiteSpace(diEndpoint) || string.IsNullOrWhiteSpace(diKey),
            "AzureAI:DocumentIntelligence:Endpoint / :Key not configured.");

        // Model under test. Bare id (e.g. "openai/gpt-4.1"); defaults to gpt-4.1.
        var chatModel = Environment.GetEnvironmentVariable("PROFILE_CHAT_MODEL") ?? "openai/gpt-4.1";
        var maxDocs = int.TryParse(Environment.GetEnvironmentVariable("PROFILE_MAX_DOCS"), out var m) && m > 0 ? m : 5;
        var skipDocs = int.TryParse(Environment.GetEnvironmentVariable("PROFILE_SKIP_DOCS"), out var sk) && sk > 0 ? sk : 0;
        var paceMs = int.TryParse(Environment.GetEnvironmentVariable("PROFILE_PACE_MS"), out var p) ? p : 20_000;

        var repoRoot = ResolveRepoRoot();
        var seedPath = Path.Combine(repoRoot, "samples", "kb");
        var dripDir = Path.Combine(repoRoot, "samples", "SampleForms", "DripOCP");
        var ocrCacheRoot = Path.Combine(AppContext.BaseDirectory, "ocr-cache");
        Directory.CreateDirectory(ocrCacheRoot);

        var pdfs = Directory.EnumerateFiles(dripDir, "*.pdf").OrderBy(x => x).Skip(skipDocs).Take(maxDocs).ToList();
        pdfs.Should().NotBeEmpty("samples/SampleForms/DripOCP must contain PDFs.");

        var diClient = new DocumentIntelligenceClient(new Uri(diEndpoint!), new AzureKeyCredential(diKey!));

        // ---- usage tallies, written by the delegating wrappers below ----
        var chatTally = new UsageTally();
        var embedTally = new UsageTally();

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GITHUB_TOKEN"] = token,
                ["Llm:Provider"] = LlmServiceCollectionExtensions.GithubModelsProvider,
                ["KnowledgeBase:SeedPath"] = seedPath,
                ["Classifier:RateLimitMaxRetries"] = "0",
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        // KB (embeddings + vector store + seeder). We register the embedding
        // generator OURSELVES first, wrapped in a usage-capturing delegator, so
        // AddIntentKnowledgeBase's own AddSingleton (which is a no-op when one is
        // already registered? no — it would add a second) ... to be safe we add
        // the KB pieces manually mirroring AddIntentKnowledgeBase but swapping in
        // the wrapped embedding generator.
        services.AddOptions<KnowledgeBaseOptions>()
            .Bind(config.GetSection(KnowledgeBaseOptions.SectionName));
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
        {
            var inner = new EmbeddingClient("openai/text-embedding-3-small",
                    new ApiKeyCredential(token!),
                    new OpenAIClientOptions { Endpoint = new Uri(LlmServiceCollectionExtensions.DefaultEndpoint) })
                .AsIEmbeddingGenerator();
            return new UsageCapturingEmbeddingGenerator(inner, embedTally);
        });
        services.AddSingleton<Microsoft.Extensions.VectorData.VectorStore>(_ =>
            new Microsoft.SemanticKernel.Connectors.InMemory.InMemoryVectorStore());
        services.AddSingleton(sp =>
        {
            var store = sp.GetRequiredService<Microsoft.Extensions.VectorData.VectorStore>();
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KnowledgeBaseOptions>>().Value;
            return store.GetCollection<string, IntentRecord>(opts.CollectionName);
        });
        services.AddSingleton<IIntentKnowledgeBase, IntentKnowledgeBase>();
        services.AddSingleton<IntentKnowledgeBaseSeeder>();

        // Chat client: raw ChatClient -> IChatClient -> [UseFunctionInvocation
        // (outer tool loop)] -> [UsageCapturingChatClient (inner: sees every
        // model round-trip)]. RetryPolicy=0 so 429s surface immediately.
        services.AddSingleton<IChatClient>(_ =>
        {
            var raw = new ChatClient(chatModel, new ApiKeyCredential(token!),
                    new OpenAIClientOptions
                    {
                        Endpoint = new Uri(LlmServiceCollectionExtensions.DefaultEndpoint),
                        RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                    })
                .AsIChatClient();
            return raw.AsBuilder()
                .UseFunctionInvocation()
                .Use(inner => new UsageCapturingChatClient(inner, chatTally))
                .Build();
        });

        services.AddSingleton<AIAgent>(sp =>
        {
            var chat = sp.GetRequiredService<IChatClient>();
            var kb = sp.GetRequiredService<IIntentKnowledgeBase>();
            return ClassifierAgentFactory.Create(chat, kb);
        });
        services.AddOptions<ClassifierOptions>().Bind(config.GetSection(ClassifierOptions.SectionName));
        services.AddSingleton<IIntentClassifier, IntentClassifier>();

        await using var sp = services.BuildServiceProvider();

        // Seed the KB (this triggers one embedding call per intent seed file —
        // captured separately as a one-time cost, NOT attributed to any doc).
        embedTally.Reset();
        var seeder = sp.GetRequiredService<IntentKnowledgeBaseSeeder>();
        await seeder.StartAsync(default);
        var seedEmbed = embedTally.Snapshot();
        Emit($"[SEED] model={chatModel}  seed embeddings: calls={seedEmbed.Calls} inputTokens={seedEmbed.InputTokens}");

        var classifier = sp.GetRequiredService<IIntentClassifier>();

        var rows = new List<DocProfile>();
        var sanitized = chatModel.Replace('/', '_').Replace(':', '_');
        var suffix = skipDocs > 0 ? $"-skip{skipDocs}" : "";
        var csvPath = Path.Combine(repoRoot, $"dripocp-token-profile-{sanitized}{suffix}.csv");

        Emit($"[START] model={chatModel}  docs={pdfs.Count}  pace={paceMs}ms  retries=off");

        var idx = 0;
        foreach (var pdfPath in pdfs)
        {
            idx++;
            var fileName = Path.GetFileName(pdfPath);
            if (idx > 1 && paceMs > 0)
            {
                Emit($"  .. pacing {paceMs}ms");
                await Task.Delay(paceMs);
            }
            Emit($"[{idx,2}/{pdfs.Count}] {fileName}");

            // ---- OCR (pages + chars). Cached across model runs. ----
            string ocrJson;
            try
            {
                ocrJson = await OcrWithCacheAsync(diClient, pdfPath, ocrCacheRoot);
            }
            catch (Exception ex)
            {
                Emit($"     OCR FAILED: {ex.GetType().Name}: {Squash(ex.Message)}");
                rows.Add(DocProfile.Failed(chatModel, fileName, "ocr-failed"));
                continue;
            }

            var pages = CountPages(ocrJson);
            var text = OcrTextFlattener.Flatten(BinaryData.FromString(ocrJson));
            var chars = text?.Length ?? 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                Emit("     OCR produced no text — skipping classify.");
                rows.Add(DocProfile.Failed(chatModel, fileName, "ocr-empty") with { OcrPages = pages });
                continue;
            }

            // ---- classify: reset per-doc tallies, run, snapshot ----
            chatTally.Reset();
            embedTally.Reset();
            var sw = Stopwatch.StartNew();
            string status = "ok";
            double topConf = 0;
            string topIntent = "";
            try
            {
                var result = await classifier.ClassifyAsync(text);
                var top = result.Intents?.OrderByDescending(i => i.Confidence).FirstOrDefault();
                if (top is not null) { topIntent = top.Intent; topConf = top.Confidence; }
                status = top?.Intent == DripOcpIntent ? "match"
                       : top is null ? "no-intent" : $"other:{top.Intent}";
            }
            catch (Exception ex)
            {
                status = $"classify-failed:{ex.GetType().Name}";
                Emit($"     CLASSIFY FAILED: {ex.GetType().Name}: {Squash(ex.Message)}");
            }
            sw.Stop();

            var chat = chatTally.Snapshot();
            var embed = embedTally.Snapshot();

            var row = new DocProfile(
                Model: chatModel, File: fileName, Status: status,
                OcrPages: pages, OcrChars: chars,
                EmbedCalls: embed.Calls, EmbedInputTokens: embed.InputTokens,
                ChatCalls: chat.Calls, ChatInputTokens: chat.InputTokens,
                ChatOutputTokens: chat.OutputTokens, ChatReasoningTokens: chat.ReasoningTokens,
                TopIntent: topIntent, TopConfidence: topConf, LlmMs: sw.ElapsedMilliseconds);
            rows.Add(row);

            Emit($"     OCR: {pages}pg/{chars}ch | embed: {embed.Calls}call/{embed.InputTokens}tok | " +
                 $"chat: {chat.Calls}call in={chat.InputTokens} out={chat.OutputTokens} " +
                 $"(reason={chat.ReasoningTokens}) total={chat.InputTokens + chat.OutputTokens} | " +
                 $"{status} {(topConf > 0 ? $"{topIntent}@{topConf:P0}" : "")} {sw.ElapsedMilliseconds}ms");

            WriteCsv(csvPath, rows);
        }

        WriteCsv(csvPath, rows);
        Emit($"[DONE] model={chatModel}  wrote {csvPath}");
        output.WriteLine($"Profiled {rows.Count} DripOCP docs on {chatModel}. CSV: {csvPath}");

        rows.Should().NotBeEmpty();
        rows.Count(r => r.Status is "match" or "no-intent" || r.Status.StartsWith("other"))
            .Should().BeGreaterThan(0, "no document completed classification — wiring or quota is broken.");
    }

    // ---------------- OCR helpers ----------------

    private static async Task<string> OcrWithCacheAsync(DocumentIntelligenceClient client, string pdfPath, string cacheRoot)
    {
        var cacheKey = $"DripOCP__{Path.GetFileNameWithoutExtension(pdfPath)}.json";
        var cachePath = Path.Combine(cacheRoot, cacheKey);
        if (File.Exists(cachePath)) return await File.ReadAllTextAsync(cachePath);

        var bytes = await File.ReadAllBytesAsync(pdfPath);
        var op = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", BinaryData.FromBytes(bytes));
        var json = BinaryData.FromObjectAsJson(op.Value).ToString();
        await File.WriteAllTextAsync(cachePath, json);
        return json;
    }

    // Document Intelligence is billed per page. Count the pages array in the
    // serialized AnalyzeResult (case-insensitive on the property name).
    private static int CountPages(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "pages", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    return prop.Value.GetArrayLength();
                }
            }
        }
        catch { /* fall through */ }
        return 0;
    }

    // ---------------- CSV ----------------

    private static void WriteCsv(string path, List<DocProfile> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("model,file,status,ocr_pages,ocr_chars,embed_calls,embed_input_tokens," +
                      "chat_calls,chat_input_tokens,chat_output_tokens,chat_reasoning_tokens," +
                      "chat_total_tokens,top_intent,top_confidence,llm_ms");
        foreach (var r in rows)
        {
            sb.Append(r.Model).Append(',').Append(r.File).Append(',').Append(r.Status).Append(',')
              .Append(r.OcrPages).Append(',').Append(r.OcrChars).Append(',')
              .Append(r.EmbedCalls).Append(',').Append(r.EmbedInputTokens).Append(',')
              .Append(r.ChatCalls).Append(',').Append(r.ChatInputTokens).Append(',')
              .Append(r.ChatOutputTokens).Append(',').Append(r.ChatReasoningTokens).Append(',')
              .Append(r.ChatInputTokens + r.ChatOutputTokens).Append(',')
              .Append(r.TopIntent).Append(',')
              .Append(r.TopConfidence.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.LlmMs).AppendLine();
        }
        try { File.WriteAllText(path, sb.ToString()); } catch { /* best effort */ }
    }

    // ---------------- progress log ----------------

    private static readonly object EmitLock = new();
    private static readonly string ProgressLogPath =
        Path.Combine(AppContext.BaseDirectory, "dripocp-token-profile-progress.log");

    private static void Emit(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
        lock (EmitLock)
        {
            try { File.AppendAllText(ProgressLogPath, stamped); } catch { }
        }
        Console.WriteLine(stamped.TrimEnd());
    }

    private static string Squash(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var one = s.Replace('\r', ' ').Replace('\n', ' ');
        return one.Length > 200 ? one[..200] + "..." : one;
    }

    private static string ResolveRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "DocProcessing.slnx")))
        {
            dir = Directory.GetParent(dir)?.FullName
                ?? throw new InvalidOperationException("Could not locate DocProcessing.slnx.");
        }
        return dir;
    }

    private sealed record DocProfile(
        string Model, string File, string Status,
        int OcrPages, int OcrChars,
        int EmbedCalls, long EmbedInputTokens,
        int ChatCalls, long ChatInputTokens, long ChatOutputTokens, long ChatReasoningTokens,
        string TopIntent, double TopConfidence, long LlmMs)
    {
        public static DocProfile Failed(string model, string file, string status) =>
            new(model, file, status, 0, 0, 0, 0, 0, 0, 0, 0, "", 0, 0);
    }

    // ============ usage-capturing delegating wrappers ============

    private sealed class UsageTally
    {
        private long _calls, _input, _output, _reasoning;
        public void AddChat(long input, long output, long reasoning)
        {
            Interlocked.Increment(ref _calls);
            Interlocked.Add(ref _input, input);
            Interlocked.Add(ref _output, output);
            Interlocked.Add(ref _reasoning, reasoning);
        }
        public void AddEmbedding(long input)
        {
            Interlocked.Increment(ref _calls);
            Interlocked.Add(ref _input, input);
        }
        public void Reset()
        {
            Interlocked.Exchange(ref _calls, 0);
            Interlocked.Exchange(ref _input, 0);
            Interlocked.Exchange(ref _output, 0);
            Interlocked.Exchange(ref _reasoning, 0);
        }
        public Snap Snapshot() => new(
            (int)Interlocked.Read(ref _calls),
            Interlocked.Read(ref _input),
            Interlocked.Read(ref _output),
            Interlocked.Read(ref _reasoning));

        public readonly record struct Snap(int Calls, long InputTokens, long OutputTokens, long ReasoningTokens);
    }

    private sealed class UsageCapturingChatClient(IChatClient inner, UsageTally tally)
        : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            var resp = await base.GetResponseAsync(messages, options, ct);
            Record(resp.Usage);
            return resp;
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, ct))
            {
                foreach (var c in update.Contents)
                {
                    if (c is UsageContent uc) Record(uc.Details);
                }
                yield return update;
            }
        }

        private void Record(UsageDetails? usage)
        {
            if (usage is null) return;
            long reasoning = 0;
            if (usage.AdditionalCounts is { } add)
            {
                foreach (var kv in add)
                {
                    if (kv.Key.Contains("reason", StringComparison.OrdinalIgnoreCase))
                    {
                        reasoning = kv.Value;
                        break;
                    }
                }
            }
            tally.AddChat(usage.InputTokenCount ?? 0, usage.OutputTokenCount ?? 0, reasoning);
        }
    }

    private sealed class UsageCapturingEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>> inner, UsageTally tally)
        : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner)
    {
        public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, Microsoft.Extensions.AI.EmbeddingGenerationOptions? options = null, CancellationToken ct = default)
        {
            var result = await base.GenerateAsync(values, options, ct);
            tally.AddEmbedding(result.Usage?.InputTokenCount ?? 0);
            return result;
        }
    }
}
