# BackOffice Document Processing — Claude Project Knowledge

Upload this file to the claude.ai "BackOffice Document Processing" Project as a knowledge file. It is the cumulative knowledge captured from working sessions, kept separately from the architectural design docs (`DocProcessing-Build-Guide.md`, `CLAUDE.md`) which already live in the repo.

Last updated: 2026-05-19.

---

## 1. The SampleForms eval test

A non-Aspire end-to-end test exists at `tests/DocProcessing.Classifier.Tests/SampleFormsClassificationTests.cs`.

It does:

1. Walk `samples/SampleForms/{DripOCP,Sell}/*.pdf` (folder names = ground-truth intents `drip_ocp` and `sell_stock`).
2. OCR each PDF via real Azure Document Intelligence (prebuilt-layout), caching the JSON under `bin/.../ocr-cache/` so reruns don't re-bill.
3. Flatten via `OcrTextFlattener` and feed the text to the production `IntentClassifier` (MAF agent + KB tools, GitHub Models).
4. For each file: log predicted intents + confidence; drop any prediction outside `{drip_ocp, sell_stock}` and flag the file `unknown -> HITL`.
5. Emit per-intent precision/recall and totals.

The full step-by-step runbook (prerequisites, env vars, `dotnet test` invocation, output reading, troubleshooting) is at the repo root: `SampleForms-Test-Runbook.md`.

## 2. Baseline accuracy (2026-05-19, paced run)

Run: gpt-4o-mini, no retries, 15s inter-file pacing, 14.6 min wall time.

| metric | value |
|---|---|
| files processed | 40 / 40 |
| matched | **25** (14 DripOCP + 11 Sell) |
| mismatched | 0 |
| unknown → HITL | 0 |
| failed (infrastructure) | 15 |

**Per-intent precision/recall on what got classified:**

| intent | TP | FP | FN | P | R |
|---|---|---|---|---|---|
| drip_ocp | 14 | 1 | 0 | 93% | 100% |
| sell_stock | 11 | 0 | 0 | 100% | 100% |

The 1 `drip_ocp` FP is from a true multi-intent Sell document where the LLM correctly co-detected DRIP signals — the folder-label ground truth doesn't capture multi-intent reality. Multi-intent detection works as specified.

## 3. Observed confidence patterns

gpt-4o-mini returns intent confidences in these bands on real production PDFs:

- **DripOCP forms: 15–45%**, clustered around 30–38%.
- **Sell forms: 33–63%**, clustered around 50–60%.

The gap is structural, not a bug: DripOCP samples are bank cheques where the DRIP context is *implicit* (the cheque itself plus a memo line); Sell forms have *explicit* lexical cues ("sell", "shares", "medallion guarantee", "stock power") that are listed as keywords in the KB definition.

Implication for HITL routing: a single confidence threshold (e.g. `< 0.5`) would route correctly-classified DripOCP files to humans. Use per-intent thresholds, or first improve the `drip_ocp` KB definition with cheque-specific keywords ("pay to the order of", "ABA routing", "memo") to lift confidences.

## 4. Failure modes seen in practice

Three different infrastructure failures dominate; none are classifier bugs.

| failure | count | cause | mitigation |
|---|---|---|---|
| HTTP 429 from GitHub Models | 11 | Per-minute RPM cap. ~3 chat + 1 embed call per file → ~10 RPM effective cap on gpt-4o-mini. Embedding calls share the bucket. | Pace requests (`SAMPLEFORMS_PACE_MS`), or re-run only failed files. Production uses Azure OpenAI with deployment-controlled limits. |
| HTTP 413 (`tokens_limit_reached`) | 2 | gpt-4o-mini context is only **8000 tokens** on GitHub Models. Some long PDFs exceed it after OCR flattening. | Use `gpt-4.1` (larger context) until daily quota hits, then switch. Production uses gpt-5.1 with 128k+ context. |
| HTTP 400 `InvalidContentLength` from Document Intelligence | 2 | F0 tier per-file size limit (~4 MB). Two PDFs in `samples/SampleForms/Sell/` exceed it. | F0 won't be prod. S0 raises limit to 500 MB. As local workaround: split PDF before sending. |

## 5. Gotchas captured in code/config

### 5a. OpenAI SDK has its own retry layer

`OpenAIClientOptions.RetryPolicy` defaults to a `ClientRetryPolicy()` with 3 retries + exponential backoff. This is **independent of** `IntentClassifier`'s Polly pipeline. Setting `ClassifierOptions.RateLimitMaxRetries = 0` only disables the outer layer — the SDK still sleeps `Retry-After` multi-second waits inside `agent.RunAsync`.

Symptom of forgetting this: test appears to hang on file 1 for 5–15 minutes per file.

Fix for eval/test code: also pass `new OpenAIClientOptions { RetryPolicy = new ClientRetryPolicy(maxRetries: 0) }` to the `ChatClient` constructor. `SampleFormsClassificationTests` bypasses `AddDocProcChatClient` and registers its own `IChatClient` to do this.

Do NOT disable SDK retries in production — transient 429s under bursty load are normal.

### 5b. OcrTextFlattener case-sensitivity

The flattener was originally case-sensitive (`pages`, `pageNumber`, `content`) but `BinaryData.FromObjectAsJson(AnalyzeResult)` from the Azure DI SDK emits **PascalCase** (`Pages`, `PageNumber`, `Content`). All real-DI OCR ran through the flattener and got empty text until this was fixed.

Now case-insensitive, with a top-level `content` field fallback for documents whose pages lack inline content. The same fix benefits both the eval test and the production pipeline (the Classifier worker reads from blob storage which also has PascalCase from the OCR engine).

### 5c. xUnit Console capture

xUnit captures `ITestOutputHelper` writes AND `Console.WriteLine` and only emits them after the test method returns. For a multi-minute eval, this looks indistinguishable from a hang.

The SampleForms test writes progress to `bin/.../sample-forms-progress.log` via `File.AppendAllText` (with a lock) so live `tail -F` works. Don't rely on Console for live progress.

Also: `PowerShell Tee-Object` defaults to UTF-16 LE which breaks `grep`. Use `File.AppendAllText` for UTF-8.

### 5d. SemanticKernel.Connectors.InMemory ↔ VectorData.Abstractions version skew

`Microsoft.SemanticKernel.Connectors.InMemory` 1.66.0-preview was built against an earlier `Microsoft.Extensions.VectorData.Abstractions` and throws `TypeLoadException: ValidateKeyProperty` when used against 10.0.0. Pin both as a pair (the repo currently uses `Connectors.InMemory` 1.74.0-preview ↔ `VectorData.Abstractions` 10.1.0).

## 6. Pacing knob

`SAMPLEFORMS_PACE_MS` (default 15000) controls inter-file delay in the SampleForms test. Lower it for faster but more rate-limited runs; raise it to 25000 for higher success rate at the cost of run time.

A single 40-document run with no failures is hard on the free tier even at 25s pacing — the embedding endpoint shares the RPM bucket. The realistic baseline is "first pass classifies ~25/40, second focused pass on the failed files (cached OCR, ~3 min) closes the gap to ~38/40, with the 2 DI-oversize files remaining permanently failed on F0."

## 7. What's NOT in this doc

The following are already authoritative elsewhere and should not be duplicated:

- Architecture, module boundaries, contracts → `CLAUDE.md` + `DocProcessing-Build-Guide.md`
- Prompts 1–14 build order → `DocProcessing-Build-Guide.md` §5
- Local↔cloud substitutions → `CLAUDE.md` "Local↔cloud substitutions"
- Test invocation step-by-step → `SampleForms-Test-Runbook.md`
- Code structure → read the repo

This file is for *cumulative knowledge from sessions* — observed behaviors, failure modes, calibration data, and gotchas that the architecture docs can't predict.
