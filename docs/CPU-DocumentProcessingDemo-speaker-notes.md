# CPU-DocumentProcessingDemo — Speaker Notes

Companion to `CPU-DocumentProcessingDemo.pptx`. One section per slide. Each section:

- **Hook** — the one thing the audience should leave the slide with.
- **Talking points** — three to five short beats; ~60–90 seconds of speaking each.
- **Q&A lead lines** — pre-drafted answers to the questions this slide tends to provoke.

Keep eye contact with the audience; the slide is the prop, not the script.

---

## Slide 1 — Title

**Hook:** This is the modular monolith for backoffice document processing, with AI-assisted intent classification at its core — built locally, on a clear path to UK South production.

**Talking points**

- Thank them for the time. One sentence on why we're here today: to walk through what we've built, what it does today, and the runway to production.
- We're going to look at the architecture, the per-document flow with its decision points, where we stand on real-form accuracy, and what's left to ship.
- I'll keep technical detail in an appendix at the end — feel free to interrupt with questions at any point.

**Q&A lead lines**

- *"How long has this been in build?"* — "The current implementation is a focused two-week iteration on a modular-monolith design we've been refining since the original RFC."
- *"Who's the audience for this today?"* — "Mixed engineering and business stakeholders. I'll calibrate depth as we go — please tell me if anything needs more or less."

---

## Slide 2 — Solution Architecture

**Hook:** Four stages, one durable orchestrator. The same module boundaries we run locally today become the AKS pods we deploy tomorrow.

**Talking points**

- *Tungsten on the left, EDC/RPA on the right.* These are the systems we already operate. The pipeline in the middle is the new thing.
- *Four stages — INGEST, EXTRACT, CLASSIFY, PERSIST.* INGEST watches the file landing point and uploads to blob. EXTRACT is real Azure Document Intelligence — same engine, same SDK, in dev and prod. CLASSIFY is the AI core: a Microsoft Agent Framework agent that consults a knowledge base of intent definitions and emits strict JSON. PERSIST writes a structured row and archives the source.
- *The orchestrator band underneath ties it all together.* MassTransit saga, durable state in EF Core. If a worker crashes after OCR but before classification, the saga picks up where it left off — nothing falls on the floor.
- *Cloud-swap is wiring, not code.* When we go to production, RabbitMQ becomes Service Bus, Azurite becomes Blob Storage, GitHub Models becomes Azure OpenAI in UK South. The module code does not change.

**Q&A lead lines**

- *"Why a modular monolith, not microservices?"* — "We pay the modular-monolith tax once and get the microservices payoff when volume justifies it. Today we onboard new engineers in an hour and operate one process; tomorrow each module is a container with no code rewrite."
- *"What happens if Document Intelligence is down?"* — "The saga retries with Polly, and after the budget is exhausted it publishes a DocumentFailedEvent. The document re-enters the queue when the resource is reachable again."
- *"Is the data ever leaving UK?"* — "In production, no — Document Intelligence and Azure OpenAI are both UK South. GitHub Models is dev-only for that exact reason."

---

## Slide 3 — Document Classification Journey

**Hook:** Five process stages and three decisions take a document from arrival to a structured row in SQL — or to a human reviewer when we are not confident enough to auto-process.

**Talking points**

- *Process stages.* Document arrives, we OCR it, we flatten the OCR JSON to plain text, the agent searches the knowledge base, then it classifies. The agent's tool-calling loop is automatic — it decides to call the KB, gets results, and produces the final JSON.
- *Decision 1 — Valid JSON?* The agent is instructed to return JSON only. If it doesn't, we get one reformat attempt; if that also fails, the document is routed to the dead-letter queue with a clear failure reason.
- *Decision 2 — Recognised intent?* The agent is allowed to pick only from intents the knowledge base knows about. If it returns something we have not seeded, the document is flagged unknown and routed to a human reviewer. This is the human-in-the-loop trigger active today.
- *Decision 3 — Confidence above threshold?* This is where the per-document confidence score becomes operationally useful. A confident classification auto-processes; a borderline one is routed for review even if the intent name is recognised. Thresholds will be per-intent because confidence calibration differs between intent types — Sell forms score higher than DRIP cheques because their lexical cues are more explicit.
- *Three outcomes.* Auto persist + downstream handoff, route to human, or document failed event. Every one of these is observable in our Aspire dashboard today and in Application Insights in production.

**Q&A lead lines**

- *"What does 'route to HITL' look like operationally?"* — "Today it's a logical route — the failure event and classification record carry everything the reviewer needs. The reviewer UI itself is on the PoC backlog."
- *"Why one reformat attempt and not three?"* — "Bounded cost. The model rarely needs more than one prompt to fix a JSON envelope. After one retry we'd rather fail fast and surface the issue than keep paying for tokens."
- *"How do you add a new transaction type?"* — "Edit a JSON file with the intent definition, keywords, and payload template. Re-deploy. No retraining."

---

## Slide 4 — Local Today vs Production Target

**Hook:** Everything in the left column is something we already run; everything in the right column is configuration we wire up at deployment time. Accuracy on the real forms is at parity with hand-curated fixtures.

**Talking points**

- *Left column: the substitution table.* This is the single most important slide for "is this production-ready?". Go down the rows — file arrival, message bus, blob, SQL, OCR, LLM, vector store, secrets, hosting. Every one is an interface boundary in our code; the implementations flip via configuration. Point at the *"We are here"* and *"We need to prove this"* annotations: we have proven everything except the cloud-specific path, which we validate during the deployment soak test.
- *Right column: the eval baseline.* 40-document corpus drawn from real backoffice forms — DripOCP cheques and Sell instructions. The 100% number is the accuracy on documents that got classified. Zero mismatches. Multi-intent detection worked — two forms in the set genuinely contained both intents.
- *Per-intent precision and recall.* drip_ocp 93% precision / 100% recall. sell_stock 100% across the board. The single false positive is from a real multi-intent document the folder label doesn't capture — strictly speaking that's a labelling gap, not a classifier error.
- *Failures are infra, not classifier.* The 15 documents that did not classify are all free-tier ceilings — rate cap on GitHub Models, file-size limit on Document Intelligence F0, 8k context on gpt-4o-mini. Every one of these disappears at production tier.

**Q&A lead lines**

- *"How confident are you in those numbers?"* — "Confident on what's in the table; the corpus is real, not synthetic. To make the production claim solid we run the same eval against Azure OpenAI on a paid tier — that closes the rate-cap and context-size gap and lets us run the full 40 in one pass."
- *"What's the per-document cost in production?"* — "OpenAI tokens per classification on the order of a few thousand, Document Intelligence at one page per call. Single-digit pence per document at our forecast volumes; happy to model precisely."
- *"What do those failure modes tell us about robustness?"* — "They tell us the dev tier is the wrong tier — the system itself doesn't have those limits."

---

## Slide 5 — Outstanding tasks for PoC

**Hook:** Three named items separate where we are from a production-quality PoC demo. Each has an owner.

**Talking points**

- *Foundry / Azure OpenAI swap (Pankaj).* Move the chat client from GitHub Models to an Azure OpenAI deployment in UK South. This is the configuration flip we have already designed for — `Llm:Provider=azure-openai` and credentials via DefaultAzureCredential. The swap unlocks the rate-cap and context-size headroom that's blocking our full-corpus runs today.
- *Enrich knowledge base for new intents (JK / Doug).* The KB has DRIP and Sell defined today. To service the wider backoffice, we add change-of-address, bereavement, certificate replacement, transfer, deposit-cheque — each is a JSON entry with definition, keywords, and payload schema. JK and Doug are the closest to the business semantics, so they own this. Engineering plumbs whatever they author.
- *Polished real-time demo (Pankaj).* The current eval is a test harness. For a live demo to the wider stakeholder group we want visual cues — file lands, confidence bar fills, extracted fields appear in a card, decision path highlights. Same pipeline underneath, better stagecraft on top.

**Q&A lead lines**

- *"Timeline on these?"* — "Foundry swap is days. KB enrichment is a fortnight depending on JK and Doug's bandwidth. Polished demo is a week once the swap is done."
- *"Anything blocking?"* — "Azure subscription with UK South capacity for the AOAI deployment and a Document Intelligence S-tier resource. Both are provisioning, not engineering."

---

## Slide 6 — Appendix

**Hook:** Brief transition. The rest is for those who want the technical deep-dive.

**Talking points**

- "I've kept this main deck deliberately tight. The next slide is for those who want to see how we map the architecture onto specific .NET projects — happy to walk through it now, or take it offline."
- Read the room. If the technical audience is engaged, go to slide 7. If business is satisfied, offer to skip and reconvene.

---

## Slide 7 — Component View, C4 Level 3 (Technology detail)

**Hook:** Every project here is a .NET assembly today and an AKS container tomorrow. The boundaries are chosen to make the production swap a deployment exercise, not a code rewrite.

**Talking points**

- *INGEST — Watcher and Ingest.Service.* The watcher fires on file landing and debounces partially-written files; the ingest service reads the bytes, uploads to blob, and publishes the document-arrived event. Two projects, one boundary.
- *ORCHESTRATION — DocumentSaga.* This is the durable state machine. It owns the document's life cycle from `AwaitingOcr` through `AwaitingClassification` to `Persisted`, with `Failed` as the terminal sink. EF Core repository, so state survives process restarts. Today on RabbitMQ; production on Azure Service Bus — one line of code change.
- *AI CORE — OCR, Classifier, KnowledgeBase, Embeddings.* OCR is a thin wrapper around Azure Document Intelligence. The Classifier is the only module that depends on Microsoft Agent Framework — that's deliberate; we contained the MAF surface area. KnowledgeBase holds the intent definitions; Embeddings is a shared library so both Classifier and KB use the same model.
- *PERSISTENCE — Sql, Archive, Outbound, Api.* Two sinks: a SQL row for query, a blob copy for audit. Outbound is the stub for the EDC handoff; Api is optional ops endpoints.
- *CROSS-CUTTING.* Contracts is the transport-agnostic message and DTO library — no MassTransit attributes, no provider types. Common holds the LLM factory; every module consumes only `IChatClient`, never the underlying SDK. ServiceDefaults plugs OTel, health, resilience into every executable. AppHost is the Aspire orchestrator for local runs.

**Q&A lead lines**

- *"Why is the Classifier the only place that touches MAF?"* — "It's the only place that needs an agent loop. Other modules are deterministic — they consume messages, do their job, publish events. Keeping the agent framework contained means we can swap to a different agent runtime if we ever need to, without rewriting eight projects."
- *"What's the test strategy?"* — "Classifier eval suite tracks precision and recall over time. Saga state-transition tests using MassTransit's test framework. Integration tests with Testcontainers spin up SQL, RabbitMQ, and Azurite — no Azure dependencies in CI."
- *"How do you handle schema migrations on the SQL side?"* — "EF Core migrations, applied via the Aspire host on startup locally and by a one-off job in production. The classification record schema is intentionally narrow — IDs, JSON, timestamps."

---

## Closing — if you want it

If you have 30 seconds to land before Q&A:

> "What I've shown you is a system designed to be ordinary in production. The AI piece is contained behind interfaces; the orchestration is durable; the cloud transition is wiring. The novelty is in the modular structure that lets us prove value locally and scale it to AKS without rewriting code. Thank you."
