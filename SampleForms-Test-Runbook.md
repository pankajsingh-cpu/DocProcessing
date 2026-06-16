# SampleForms Classification Test — Local Run Runbook

Run the classifier end-to-end (minus messaging + SQL) against the real PDFs in
`samples/SampleForms/{DripOCP,Sell}/`. The test calls Azure Document
Intelligence for OCR, the production `IntentClassifier` (real MAF agent +
GitHub Models GPT-4.1) for intent detection, and reports per-file predicted
intents, confidence, unknowns routed to HITL, and per-intent precision/recall.

Test location: `tests/DocProcessing.Classifier.Tests/SampleFormsClassificationTests.cs`.

---

## 1. Prerequisites

| Need | Where | How to get it |
|---|---|---|
| .NET 10 SDK | local | `winget install Microsoft.DotNet.SDK.10` |
| GitHub PAT with `models:read` scope | github.com/settings/tokens (Classic) | Generate new token; only `models:read` is required |
| Azure Document Intelligence resource | Azure portal | Create a Document Intelligence (Form Recognizer) resource. F0 (free) tier is fine for ~500 pages/month. Note Endpoint + Key from "Keys and Endpoint". |

No Docker, no Aspire, no SQL, no RabbitMQ, no Azurite required — this test
bypasses messaging/persistence and exercises only OCR + Classifier directly.

---

## 2. One-time setup

All three credentials live in the test project's user-secrets store. Set them
once and every future shell can run the test with no further configuration.
(Env vars still work as a fallback — see §2c — but user-secrets is the
recommended path.)

### 2a. Note your Document Intelligence values

From the Azure portal, "Keys and Endpoint" blade on your Document Intelligence
resource:

- Endpoint, e.g. `https://<name>.cognitiveservices.azure.com/`
- Key 1 or Key 2 (either works)

### 2b. Store all three credentials in user-secrets (recommended)

```powershell
# GitHub Models PAT (models:read scope only)
dotnet user-secrets set "GITHUB_TOKEN" "ghp_xxxxxxxx" `
    --project tests/DocProcessing.Classifier.Tests

# Document Intelligence
dotnet user-secrets set "AzureAI:DocumentIntelligence:Endpoint" "https://<name>.cognitiveservices.azure.com/" `
    --project tests/DocProcessing.Classifier.Tests
dotnet user-secrets set "AzureAI:DocumentIntelligence:Key" "<key>" `
    --project tests/DocProcessing.Classifier.Tests

# Verify
dotnet user-secrets list --project tests/DocProcessing.Classifier.Tests
```

User-secrets live in `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`
on Windows — outside the repo, never committed.

### 2c. Env-var fallback (CI or ad-hoc)

If you cannot use user-secrets (e.g. on a CI runner), set the same values as
process env vars. The test reads user-secrets first, env vars second:

```powershell
$env:GITHUB_TOKEN = "ghp_..."
$env:AzureAI__DocumentIntelligence__Endpoint = "https://<name>.cognitiveservices.azure.com/"
$env:AzureAI__DocumentIntelligence__Key = "<key>"
```

(Note the env-var name convention uses double-underscore `__` in place of the
colon `:` used in user-secrets — that is .NET's configuration binding rule.)

---

## 3. Run the test

Open a PowerShell terminal at the repo root.

### 3a. Build (once)

```powershell
dotnet build tests/DocProcessing.Classifier.Tests
```

### 3b. Set credentials for this shell

If you completed §2b (user-secrets), skip this step — the test reads the values
automatically. Only set env vars if you took the CI fallback path in §2c:

```powershell
$env:GITHUB_TOKEN = 'ghp_xxxxxxxx'
$env:AzureAI__DocumentIntelligence__Endpoint = 'https://<name>.cognitiveservices.azure.com/'
$env:AzureAI__DocumentIntelligence__Key = '<key>'
```

### 3c. Invoke the test

```powershell
dotnet test tests/DocProcessing.Classifier.Tests `
    --no-build `
    --filter "FullyQualifiedName~SampleFormsClassificationTests" `
    --logger "console;verbosity=detailed"
```

What happens:

1. For each PDF in `samples/SampleForms/DripOCP/` and `samples/SampleForms/Sell/`,
   the test downloads (or loads from cache) the Document Intelligence
   `prebuilt-layout` response.
2. The cached JSON is written to
   `tests/DocProcessing.Classifier.Tests/bin/Debug/net10.0/ocr-cache/{Group}__{file}.json`
   so subsequent runs do not re-bill Document Intelligence.
3. The OCR JSON is flattened to plain text via the production `OcrTextFlattener`
   and fed to the production `IntentClassifier` (KB-grounded MAF agent against
   GitHub Models GPT-4.1).
4. For each file the test logs predicted intents + confidence. Any prediction
   outside `{drip_ocp, sell_stock}` is dropped and the file is flagged
   `unknown -> HITL`.
5. A per-intent precision/recall summary and a totals block (matched /
   mismatched / unknown / failed) is printed at the end.

The test self-skips with a clear message if either credential is missing — no
fatal error.

---

## 4. Reading the output

Each file emits one of:

```
-- DripOCP/26002WF00175356.pdf (expected: drip_ocp) --
  predicted: [drip_ocp@95%]  status=match  llm=4321ms
```

```
-- Sell/25275WF00298698.pdf (expected: sell_stock) --
  predicted: (none recognised) raw=[change_of_address@40%] -> unknown, routing to HITL
```

```
-- DripOCP/...pdf (expected: drip_ocp) --
  predicted: [sell_stock@70%]  status=mismatch  llm=3892ms
```

```
-- Sell/...pdf (expected: sell_stock) --
  OCR FAILED: RequestFailedException: ...InvalidContentLength...
```

Followed by the summary:

```
== Per-intent precision / recall ==
  drip_ocp      TP=18  FP=1  FN=2   P=95%  R=90%
  sell_stock    TP=17  FP=2  FN=1   P=89%  R=94%

Files total:    40
Matched:        35
Mismatched:     3
Unknown (HITL): 1
Failed:         1
```

The xUnit assertion only fails if **every** file failed (wiring broken). Imperfect
precision/recall does not fail the test — this is a tracking harness, not a
gate.

---

## 5. Re-runs and cost

- **OCR is cached** under `bin/Debug/net10.0/ocr-cache/`. After the first run,
  re-runs only hit GitHub Models, not Document Intelligence.
- **Force a re-OCR**: delete the `ocr-cache/` folder.
- **GitHub Models free-tier limits** on `openai/gpt-4.1` are tight: ~50 req/day,
  10 RPM, 2 concurrent. The agent's tool-call loop (`search_intent_kb` →
  `get_intent_schema` → final answer) makes 2–3 calls per document, so 40
  documents ≈ 80–120 calls — over the daily quota. If you hit HTTP 429:
  - Polly inside `IntentClassifier` retries up to 3 times honoring
    `Retry-After`; you'll see warnings in the log.
  - After 3 retries the file is logged as `CLASSIFY FAILED` and counted
    against the `Failed:` total.
  - Workaround: subset the run by temporarily removing files from the sample
    folders, or switch to a higher-quota model — edit
    `src/DocProcessing.AppHost/AppHost.cs` `ChatModel` to `openai/gpt-4o-mini`
    (these env vars affect the worker, not this test, so for the test you'd
    also need to extend the in-memory config in
    `SampleFormsClassificationTests.cs` with `["Llm:ChatModel"] = "openai/gpt-4o-mini"`).

---

## 6. Common errors

| Symptom | Cause | Fix |
|---|---|---|
| `Skipped: GITHUB_TOKEN env var not set` | Step 3b missed | Set `$env:GITHUB_TOKEN` and re-run |
| `Skipped: AzureAI__DocumentIntelligence__Endpoint / __Key env vars not set` | Step 3b missed | Set both DI env vars and re-run |
| `OCR FAILED: ...InvalidContentLength...` | PDF over the DI free-tier per-file size limit (~4 MB on F0, 500 MB on S0) | Either upgrade DI tier or skip that file |
| `CLASSIFY FAILED: ClientResultException ... 429` (after retries) | GitHub Models daily quota hit | Wait ~24h or switch model per §5 |
| `Test Run Failed` with all files `OCR produced no text` | Stale build of `OcrTextFlattener` (pre case-insensitive fix) | `dotnet build` and re-run |
| `TypeLoadException: ValidateKeyProperty` | Stale `Microsoft.SemanticKernel.Connectors.InMemory` version | Already pinned in `Directory.Packages.props` to a version compatible with `Microsoft.Extensions.VectorData.Abstractions` — `dotnet restore` and rebuild |

---

## 7. Quick reference — one-shot block

If you've completed section 2b (user-secrets), the test runs with no further
setup — just:

```powershell
dotnet test tests/DocProcessing.Classifier.Tests `
    --filter "FullyQualifiedName~SampleFormsClassificationTests" `
    --logger "console;verbosity=detailed"
```
