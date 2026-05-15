# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository status

This repo currently contains **design artifacts only** — no source code yet:

- `DocProcessing-Build-Guide.md` — the authoritative step-by-step build plan (read this before scaffolding anything)
- `DocProcessing_C4_L3_updated.drawio` — C4 L3 architecture diagram; every box on the diagram maps 1:1 to a .NET project
- `RFC_DocProcessing_AI_updated.docx` — the RFC behind the design

When generating code, follow the prompts in §5 of the build guide **in order** (Prompt 1 → Prompt 14). Each prompt assumes the previous have run.

## What is being built

A backoffice document-processing pipeline for Computershare. **Modular monolith first, AKS pods later** — every module is already shaped so it can be peeled off into its own container with no code change.

Stack: **.NET 9 · Microsoft Agent Framework (MAF) 1.x · MassTransit · .NET Aspire · Azurite · GitHub Models (dev) → Azure OpenAI (prod)**.

Pipeline: `watcher → ingest → orchestrator (saga) → ocr → orchestrator → classifier (MAF agent) → orchestrator → persistence (SQL + archive blob) → outbound workflow handoff`.

## Architectural invariants (do not violate)

These are load-bearing decisions from the design — preserve them when generating or modifying code:

1. **Module = future pod.** Each `DocProcessing.X` project is a separate Worker + DI scope today and a separate AKS container tomorrow. Don't merge projects or share DbContexts across module boundaries.
2. **Contracts are transport-agnostic.** `DocProcessing.Contracts` holds plain immutable records (events, commands, DTOs). **No MassTransit attributes**, no provider types. Every other module references it.
3. **LLM provider goes through `DocProcessing.Common` factories only.** `AddDocProcChatClient` / `AddDocProcEmbeddings` switch on `Llm:Provider` (`github-models` vs `azure-openai`). Module code consumes only `IChatClient` / `IEmbeddingGenerator<string, Embedding<float>>` — never construct `ChatClient` or `EmbeddingClient` directly outside Common, and never hardcode `https://models.github.ai/inference` outside Common.
4. **MAF tool loop is automatic.** `IChatClient.AsBuilder().UseFunctionInvocation()` runs the entire tool-calling loop. Do not hand-roll dispatch of `search_intent_kb` / `get_intent_schema`.
5. **Classifier output is strict JSON.** The system prompt forbids prose/markdown. The consumer deserializes with `JsonOpts.StrictCamelCase`; on parse failure, retry once with a "reformat as valid JSON only" follow-up, then publish `DocumentFailedEvent`.
6. **GitHub Models rate limits are tight** (~50 req/day, 10 RPM, 2 concurrent on high-tier models). Always provide an eval/stub mode that mocks the LLM so saga and persistence work doesn't burn the daily quota. Handle HTTP 429 with Polly honoring `Retry-After` (up to 3 retries, then `DocumentFailedEvent` with `stage="rate-limited"`).
7. **OCR has a STUB mode.** `OCR_MODE=stub` returns fixtures from `samples/ocr-fixtures/`, so the pipeline is runnable without an Azure Document Intelligence resource.
8. **Saga state survives restarts.** Use the EF Core saga repository against the SQL container — no in-memory saga repository.
9. **Cloud swap is wiring-only.** The table in §4 Step 13 of the build guide enumerates every local→cloud swap (RabbitMQ→ASB, Azurite→Blob, InMemoryVectorStore→AI Search, GITHUB_TOKEN→DefaultAzureCredential, etc.). New code must not introduce anything that would require a logic change to deploy to AKS — Prompt 14 audits for exactly these violations.

## Commands

The solution does not exist yet. Once scaffolded per Prompt 1, the standard commands will be:

```powershell
# One-time prerequisites
winget install Microsoft.DotNet.SDK.9
dotnet workload install aspire
winget install Docker.DockerDesktop

# GitHub Models PAT (scope: models:read) — required by Classifier and KnowledgeBase
dotnet user-secrets init --project src/DocProcessing.AppHost
dotnet user-secrets set "GITHUB_TOKEN" "ghp_..." --project src/DocProcessing.AppHost

# Run the full local stack (Aspire dashboard opens)
docker desktop start
dotnet run --project src/DocProcessing.AppHost

# Build / test
dotnet build
dotnet test
dotnet test tests/DocProcessing.Classifier.Tests          # agent eval suite
dotnet test --filter "FullyQualifiedName~SagaTests"       # single test class
```

End-to-end smoke test: drop a `.tif` into `_landing/`, then `SELECT * FROM ClassificationRecord` in the SQL container — a row should appear within ~5s.

## Module map

Source is laid out as `src/DocProcessing.<Module>/`:

| Module | Type | Role |
|---|---|---|
| `AppHost` | Aspire host | Wires SQL, Azurite, RabbitMQ, and all module projects; injects `GITHUB_TOKEN` and LLM env vars |
| `ServiceDefaults` | Aspire shared | OTel, health, resilience — referenced by every executable |
| `Contracts` | classlib | Events, commands, DTOs, `JsonOpts.StrictCamelCase` — transport-agnostic |
| `Common` | classlib | `AddDocProcChatClient` / `AddDocProcEmbeddings` factories; cross-cutting infra |
| `Ingest.Watcher` | worker | `FileSystemWatcher` on `_landing/` → publishes `BatchArrivedEvent` (replaced by Event Grid in cloud) |
| `Ingest.Service` | worker | Splits multi-page TIF (Magick.NET) → blobs → `DocumentIngestedEvent` |
| `Orchestration` | worker | MassTransit saga `DocumentSaga` with EF Core repository |
| `Ocr` | worker | `Azure.AI.DocumentIntelligence` consumer of `RunOcrCommand`; supports `OCR_MODE=stub` |
| `Classifier` | worker | MAF `AIAgent` consumer of `ClassifyCommand` — the only module that depends on MAF |
| `KnowledgeBase` | worker | Seeds intents into `InMemoryVectorStore`; exposes `IIntentKnowledgeBase` (used as MAF tools) |
| `Embeddings` | classlib | Shared by Classifier and KnowledgeBase |
| `Persistence.Sql` | classlib | `ClassificationRecord` + `DocProcDbContext` + migrations |
| `Persistence.Archive` | classlib | Blob copy `documents/{id}.tif` → `archive/{yyyy}/{MM}/{id}.tif` |
| `Outbound.Workflow` | minimal API | Stub `POST /workflow/handoff` today; real EDC/RPA client later |
| `Api` | minimal API | Optional ops/replay endpoints |

Tests under `tests/`: `Classifier.Tests` (intent eval suite, tracks precision/recall per intent), `Orchestration.Tests` (saga transitions via `MassTransit.TestFramework`), `IntegrationTests` (Aspire.Hosting.Testing + Testcontainers, no Azure deps).

## Local↔cloud substitutions

When reading or writing wiring code, keep this mapping in mind — anything hardcoded against the local side is a bug that Prompt 14 will flag:

| Local | Cloud |
|---|---|
| `FileSystemWatcher` on `_landing/` | Azure Files + Event Grid subscription |
| RabbitMQ (`UsingRabbitMq`) | Azure Service Bus (`UsingAzureServiceBus`) |
| Azurite (`storage.RunAsEmulator()`) | Real `BlobServiceClient` + managed identity |
| SQL Server container | Azure SQL + managed identity |
| `InMemoryVectorStore` | `AzureAISearchVectorStore` (consume only `IVectorStore`) |
| GitHub Models `ChatClient`/`EmbeddingClient` at `https://models.github.ai/inference` | `AzureOpenAIClient(uri, DefaultAzureCredential()).GetChatClient("gpt-5.1")` |
| `GITHUB_TOKEN` user-secret | `DefaultAzureCredential` (Workload Identity on AKS) |

## Region/compliance constraint

GitHub Models has no UK region — traffic routes through US datacentres, so it is **dev-only**. Production runs on Azure OpenAI in **UK South (Slough)** co-located with Document Intelligence to avoid cross-region egress. Don't propose GitHub Models for any prod path.
