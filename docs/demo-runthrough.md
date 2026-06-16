# Backoffice Document Processing — Live Demo Runthrough

Step-by-step script for showing the current solution to a mixed stakeholder audience.

- Two phases, ~12 minutes total wall time.
- **Phase 1 — Console runner (2 minutes wall time)**: classify 4 cherry-picked production-shape PDFs, show predicted intents and confidences in real time.
- **Phase 2 — Aspire dashboard live drop (5–7 minutes wall time)**: bring up the full local stack, drop a PDF into the watched folder, watch the trace propagate through every module, query the SQL row at the end.

Designed to be presenter-driven on a laptop with a projector. Self-contained: every command is copy-pasteable.

Companion to `docs/presentation-local-vs-production.md` (the slide deck) — talk through the deck first, then this runthrough proves the claims.

---

## 0 · Pre-flight checklist — do this BEFORE the meeting

Run all of these the morning of the demo. Each item is a one-time setup; assume nothing.

### 0.1 — Machine state

```powershell
# 1. .NET SDK is installed
dotnet --version
# expect: 10.0.x

# 2. Docker Desktop is running
docker info | Select-String "Server Version"
# expect: a version line, not a connection error
```

If Docker isn't running, start Docker Desktop and wait for the whale icon to settle (~30 seconds) before proceeding.

### 0.2 — Credentials

```powershell
# All three credentials live in the test project's user-secrets store. Verify:
dotnet user-secrets list --project tests/DocProcessing.Classifier.Tests
# expect three lines:
#   GITHUB_TOKEN = ...
#   AzureAI:DocumentIntelligence:Endpoint = https://...
#   AzureAI:DocumentIntelligence:Key = ...
```

If any are missing, see `SampleForms-Test-Runbook.md` §2b. Once set, every
future shell (including the demo) picks them up automatically — no env-var
juggling on stage.

### 0.3 — Build everything (warm caches)

```powershell
dotnet build
# expect: Build succeeded, 0 errors. ~60 seconds the first time, ~5 seconds thereafter.
```

This warms the build cache so the live demo doesn't pay for a cold build.

### 0.4 — Warm the OCR cache

The Phase 2 live drop uses Document Intelligence, which takes 5–10 seconds per page. Warm the cache once so it lands in <1 second on stage.

```powershell
# Run the SampleForms test with a tiny subset and the OCR cache it creates
# is reused by the live demo too.
$env:SAMPLEFORMS_MAX_FILES_PER_FOLDER = '2'
$env:SAMPLEFORMS_PACE_MS = '15000'
dotnet test tests/DocProcessing.Classifier.Tests `
    --filter "FullyQualifiedName~SampleFormsClassificationTests" `
    --no-build `
    --logger "console;verbosity=detailed"
# expect: 4 files processed in ~80 seconds, 4 matched
```

### 0.5 — Pick the live-drop PDF

Identify one or two PDFs you will drop during Phase 2. Recommended cherry-picks (already OCR-cached after step 0.4):

- `samples/SampleForms/Sell/25275WF00298702.pdf` — highest-confidence Sell result (62%)
- `samples/SampleForms/DripOCP/26002WF00175357.pdf` — clean DRIP cheque (45%)

Copy these two files to your desktop or another easy-to-reach location for the live drop. **Don't** drop the originals — keep the `samples/SampleForms` folder pristine.

### 0.6 — Restart any stuck containers

```powershell
docker ps -a --filter "name=apphost"
# kill any leftover containers from previous demos
docker ps -a -q --filter "name=apphost" | ForEach-Object { docker rm -f $_ }
```

You are now ready.

---

## Phase 1 — Console runner (target: 2 minutes on screen)

**The pitch:** "Here are four production-shape forms from our sample corpus. The classifier reads each one and tells us what it is and how confident it is. Watch."

### 1.1 — Open a clean terminal window at the repo root

Use a large font (24pt+) so the projector audience can read. Maximise the window. Clear with `cls`.

### 1.2 — Open the progress log in a second tail window (optional)

In a second terminal:

```powershell
Get-Content -Wait "tests/DocProcessing.Classifier.Tests/bin/Debug/net10.0/sample-forms-progress.log"
```

This streams progress to a window that is easier to read than xUnit's noisy output. Place it on the projector while you keep the main terminal off-screen.

### 1.3 — Set up the demo knobs

Credentials are already in user-secrets (§0.2). Only the two runtime knobs need
exporting:

```powershell
$env:SAMPLEFORMS_MAX_FILES_PER_FOLDER = '2'   # 2 DripOCP + 2 Sell = 4 files
$env:SAMPLEFORMS_PACE_MS = '15000'            # safely under GitHub Models 10 RPM
```

### 1.4 — Hit go

```powershell
dotnet test tests/DocProcessing.Classifier.Tests `
    --filter "FullyQualifiedName~SampleFormsClassificationTests" `
    --no-build `
    --logger "console;verbosity=detailed"
```

### 1.5 — What to narrate while it runs

| Time | Log line you'll see | What to say |
|---|---|---|
| 0:00 | `[START] 4 files queued` | "Four real-shape forms from the corpus — two DRIP cheques, two share-sale instructions." |
| 0:05 | `[ 1/4] RUN  DripOCP/...` | "It just downloaded the OCR result from blob — that's cached after the first OCR run." |
| 0:13 | `-> predicted: [drip_ocp@38%] match` | "Identified as a DRIP / optional cash purchase. 38% confidence is on the low side because cheque cues are implicit; we have a tuning plan for this." |
| 0:30 | `[ 2/4] RUN  ...` | "The 15-second gap is deliberate — we are pacing to stay under the free-tier rate limit. In production this disappears." |
| 1:30 | `Matched: 4 ... Failed: 0` | "Four for four. The KB knows about two intents today — DRIP and share sale — and routed correctly on all four. Real-form precision is 100% on what we classify." |

### 1.6 — Expected outcome

```
== Per-intent precision / recall ==
  drip_ocp      TP=2   FP=0   FN=0    P=100%  R=100%
  sell_stock    TP=2   FP=0   FN=0    P=100%  R=100%

Files total:    4
Matched:        4
Mismatched:     0
Unknown (HITL): 0
Failed:         0
```

### 1.7 — If something fails on stage

- **HTTP 429** on file 1 within seconds: the GitHub Models per-minute window is hot. Pause for 60 seconds and re-run. Or pivot directly to Phase 2.
- **"Skip: GITHUB_TOKEN not set"**: step 1.3 didn't run correctly. Re-run the assignment line.
- **`OCR FAILED`**: the OCR cache was deleted; the live OCR will retry. Add 5 seconds to your timing.

---

## Phase 2 — Aspire dashboard live drop (target: 5–7 minutes on screen)

**The pitch:** "That was the classifier in isolation. This is the full pipeline — every module running in its own process, every message going through a real queue, every result landing in a real database. Watch."

### 2.1 — Start the full local stack

In the main terminal (clear it first):

```powershell
dotnet run --project src/DocProcessing.AppHost
```

Wait for the Aspire dashboard URL to print (~10 seconds), then `Ctrl+click` it or open it in a browser. Maximise the browser on the projector.

The dashboard shows every service as a tile:

- `sql` (SQL Server) — wait for green
- `storage` (Azurite blob emulator) — wait for green
- `messaging` (RabbitMQ) — wait for green
- `watcher`, `ingest`, `orchestrator`, `ocr`, `classifier`, `kb`, `outbound`, `api` — wait for green

Total warm-up: ~30–60 seconds. **Do this part during your closing remarks for Phase 1** so the audience doesn't watch a progress bar.

### 2.2 — Open three useful views

In separate browser tabs from the dashboard:

- **Traces** — shows distributed traces. Filter by service to clean up.
- **Logs** — live log stream. Filter to the `classifier` service for the most interesting view.
- **Resources** — the tiles. Useful for the dashboard's CPU/memory view at the end.

### 2.3 — Drop the live file

Open File Explorer at the repo root, navigate to `_landing/`. Drag the pre-prepared `25275WF00298702.pdf` from your desktop into `_landing/`.

### 2.4 — What to narrate

| Wall-time after drop | Where to look | What to say |
|---|---|---|
| 0–2s | Logs (`watcher`) | "The watcher saw the file. It is now publishing a `BatchArrivedEvent` to RabbitMQ — that becomes Service Bus in production." |
| 2–5s | Logs (`ingest`) | "Ingest read the file, uploaded it to blob, and published `DocumentIngestedEvent`. The orchestrator is now starting the saga." |
| 5–10s | Logs (`orchestrator`, `ocr`) | "Saga sent `RunOcrCommand` to the OCR worker. OCR is hitting our cached result — in a cold path this is a 5-second call to Azure Document Intelligence." |
| 10–18s | Logs (`classifier`) | "Classifier is running the agent loop now. Watch the tool calls — first `search_intent_kb`, then `get_intent_schema`, then the final JSON response." |
| 18–25s | Traces | "Here is the distributed trace — every step on one timeline. This is what App Insights gives us in production for free." |

### 2.5 — Show the persistence proof

Open a SQL terminal (recommended: VS Code SQL extension, or `sqlcmd` from the Aspire dashboard's SQL tile).

```sql
SELECT TOP 5
  DocumentId,
  TransactionType,
  Confidence,
  CreatedDate
FROM ClassificationRecord
ORDER BY CreatedDate DESC;
```

Expected result: a row for the file you just dropped, with `TransactionType = 'sell_stock'`. Take a moment to point out:

- The full classified intent and extracted fields are in the `Intents` and `ExtractedFields` JSON columns — point at them but don't open in detail.
- The archive blob is at `archive/2026/05/{docId}.tif` in Azurite — open the blob tab in the dashboard to show it.

### 2.6 — Show one failure path (optional, 1 minute)

If you have time, drop a deliberately-failing file (e.g. `samples/SampleForms/Sell/25275WF00321204.pdf`, which exceeds DI's free-tier size limit). Watch the trace turn red and the `DocumentFailedEvent` propagate. This proves the failure path is observable.

### 2.7 — Shut down

```powershell
# In the terminal running AppHost: Ctrl+C and wait for graceful shutdown (~5s)
```

---

## 3 · Q&A cheat sheet

Anticipated questions and the line to lead with.

| Question | Lead line |
|---|---|
| *"Is this safe for our regulatory posture?"* | "Production runs entirely in Azure UK South. Document Intelligence and Azure OpenAI are both co-located in Slough; no cross-region egress, no US datacentre. GitHub Models is dev-only for that exact reason." |
| *"What if the AI is wrong?"* | "Every result carries a confidence. We have an HITL filter today — any document the classifier can't recognise (or, with one config change, any document below your chosen confidence threshold) is routed to a human reviewer instead of auto-processed." |
| *"What does it cost in production?"* | "AI cost is per-token on Azure OpenAI — measurable per-document and budgetable per-month. Document Intelligence is per-page. Total per-document cost on the order of a few pence at our forecast volumes; happy to model this precisely with your volume curve." |
| *"How do we add a new transaction type?"* | "Add a row to the intent KB seed JSON: name, definition, keywords, payload schema. Re-deploy. No model retraining, no prompt engineering on the agent. New intent is live the next time the KB seeds." |
| *"What is the failure mode if Azure OpenAI is down?"* | "Saga is durable — documents queue up in Service Bus and resume when the model is reachable. If the outage is long, we have an `eval/stub` mode that mocks the LLM so the rest of the pipeline can continue, with documents flagged for re-classification later." |
| *"How does this fit our existing Tungsten / EDC / RPA stack?"* | "Tungsten is the upstream input — feeds files into our landing folder today, Event Grid into Azure Files in production. EDC is the downstream — our outbound handoff is a stub today that becomes a real HTTP call into your endpoint, contract already defined." |
| *"What if we want a different AI provider?"* | "The factory in `DocProcessing.Common.Llm` is the only place that knows what provider we're using. Adding (say) Anthropic or AWS Bedrock is implementing one factory method — every module above it sees only `IChatClient`." |
| *"How long until production?"* | "Weeks, not months, because we have already paid the modular-monolith tax. The cloud-swap is configuration, not code; the work is mostly provisioning, networking, identity, and the EDC contract." |
| *"Why not fine-tune a model?"* | "Fine-tuning locks us to a specific model snapshot and requires curated training data per transaction type. Agent + KB lets us add a transaction type by editing a JSON file — and the KB swap (in-memory to Azure AI Search) gives us hybrid semantic + keyword search out of the box." |

---

## 4 · Appendix — quick commands

Once you have done the demo a few times, this is the cheat sheet you actually need. Everything above is for the first run.

```powershell
# Phase 1 — credentials come from user-secrets; no env-var setup needed
$env:SAMPLEFORMS_MAX_FILES_PER_FOLDER = '2'
$env:SAMPLEFORMS_PACE_MS = '15000'
dotnet test tests/DocProcessing.Classifier.Tests `
    --filter "FullyQualifiedName~SampleFormsClassificationTests" `
    --no-build --logger "console;verbosity=detailed"

# Phase 2
dotnet run --project src/DocProcessing.AppHost
# (drop a PDF into _landing/, watch dashboard, query SQL)
```
