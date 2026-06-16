# DripOCP Token-Usage Profile — Results (gpt-4.1)

**Run date:** 2026-06-08 · **Branch:** Iteration1 · **Provider:** GitHub Models (dev endpoint)
**Model profiled:** `openai/gpt-4.1` · **Harness:** `tests/DocProcessing.Classifier.Tests/DripOcpTokenProfilingTests.cs`
**Raw data:** `dripocp-token-profile-openai_gpt-4.1*.csv` (one row per document)

This profiles every **external foundry resource** the DripOCP pipeline touches —
OCR, embeddings (vector encoding), and the chat LLM — and records per-document
**input vs output** token usage so cost can be extrapolated across solution
architectures.

> **Scope note.** gpt-4o-mini was dropped from this analysis (erratic KB
> over-looping made its per-doc cost unpredictable and it isn't the target
> architecture). The requested gpt-4.6 / 5.1 / 5.2 don't exist in the GitHub
> Models catalog, and gpt-5 / o4-mini are entitlement-blocked on this dev token
> (400 `unavailable_model`) — gpt-5.x numbers require the Azure OpenAI prod path.
> **gpt-4.1 is the sole, stable basis below.**

Cleardown before the run: deleted `ocr-cache/` (38 cached responses),
`sample-forms-progress.log`, and repo-root `sample-forms-run.log`.

---

## 1. Per-resource token usage, per DripOCP document (gpt-4.1, n=8 clean docs)

gpt-4.1 is deterministic on these forms: **exactly 3 chat calls/doc, 0 reasoning
tokens**, every document. The distribution is tight (LLM total sd = 273 on a
~4,550 mean, ±6%).

### OCR — Azure Document Intelligence `prebuilt-layout`
Billed **per page, not per token** — no token figure exists.

| Metric | Value |
|---|---|
| Pages / doc | **2** (constant: mean = median = mode = 2) |
| Flattened text / doc | mean ~1,052 chars (range 367–1,434) |
| Billing unit | **2 page-units / DripOCP doc** |

### Embeddings — `text-embedding-3-small` (vector encoding)
One call per `search_intent_kb`. **Input-only — embeddings emit 0 output tokens.**
One-time KB seed embedding = **221 tokens** (amortised, not per-doc).

| | Input tokens/doc | Output tokens/doc |
|---|---|---|
| Mean | **52.0** | 0 |
| Median | 47.0 | 0 |
| Std dev | 18.2 | 0 |
| Min–Max | 32 – 83 | — |

### Chat LLM — `gpt-4.1`

| | **Input tokens/doc** | **Output tokens/doc** | LLM total/doc |
|---|---|---|---|
| **Mean** | **4,297.4** | **254.0** | 4,551.4 |
| **Median** | **4,368.0** | **249.0** | 4,606.0 |
| **Mode** | none (all unique)* | none (all unique)* | none* |
| Std dev | 264.9 | 17.9 | 273.4 |
| Min–Max | 3,748 – 4,678 | 235 – 285 | 3,985 – 4,954 |

\* Mode is undefined for token counts (continuous, every doc differs). It is only
meaningful on discrete sub-metrics: **OCR pages mode = 2**, **chat-calls mode = 3**.

---

## 2. Summary — input vs output per DripOCP document

| Resource | Input tok/doc (mean) | Output tok/doc (mean) | Total tok/doc |
|---|---|---|---|
| Chat LLM (gpt-4.1) | 4,297.4 | 254.0 | 4,551.4 |
| Embeddings | 52.0 | 0 | 52.0 |
| **All tokens** | **4,349.4** | **254.0** | **4,603.4** |
| OCR (DI) | — (per page) | — | 2 pages |

Split: **~94.5% input · ~5.5% output**. Output is small and stable because the
classifier emits compact strict-JSON (no prose).

---

## 3. Extrapolation to 100,000 pages

**Conversion:** the sample is uniformly **2 pages/doc**, so
**100,000 pages ≈ 50,000 DripOCP documents.** The LLM and embedding calls fire
**once per document**, so they scale with documents; OCR scales with pages.

### Per-page unit rates (gpt-4.1)
| Resource | Input tok/page | Output tok/page |
|---|---|---|
| Chat LLM | 2,148.7 | 127.0 |
| Embeddings | 26.0 | 0 |
| **Total** | **2,174.7** | **127.0** |

### Projected totals for 100,000 pages (50,000 docs)

| Resource | **Input tokens** | **Output tokens** | Total tokens |
|---|---|---|---|
| **Chat LLM (gpt-4.1)** | **214.87 M** | **12.70 M** | **227.57 M** |
| **Embeddings** | **2.60 M** | 0 | 2.60 M |
| **Grand total (tokens)** | **217.47 M** | **12.70 M** | **≈ 230.17 M** |
| **OCR (Document Intelligence)** | — | — | **100,000 pages** (per-page billed) |
| KB seed embedding | — | — | 221 tokens (one-time, negligible) |

- Mean-based grand total: **≈ 230.2 M tokens**; median-based cross-check:
  **≈ 233.2 M** (within ~1.3% — the distribution is tight enough that mean and
  median agree, so the projection is robust).
- Of the 230 M: **~93.4% LLM input, ~5.5% LLM output, ~1.1% embedding input.**
- **OCR is a separate per-page line item** (100,000 pages on Document
  Intelligence) and carries no token cost.

> **Caveat — page-count assumption.** This linear projection assumes the
> production mix averages the observed **2 pages/doc**. The LLM input has a large
> *fixed* component (system prompt + KB schema ≈ 3,500–3,700 tok) plus a smaller
> per-content component, so cost tracks **documents**, not pages. If real DripOCP
> documents average *k* pages, use **docs = 100,000 / k** and multiply the
> per-doc means in §2: e.g. 4-page docs → 25,000 docs → ~115 M tokens; 1-page
> docs → 100,000 docs → ~460 M tokens. Re-profile if the real page distribution
> differs materially from 2.

---

## 4. Per-document raw data (gpt-4.1 clean docs)

| Doc | OCR pages | OCR chars | embed in | LLM in | LLM out | LLM total | conf |
|---|---|---|---|---|---|---|---|
| 175356 | 2 | 1,207 | 33 | 4,364 | 235 | 4,599 | 92% |
| 175357 | 2 | 1,068 | 50 | 4,297 | 257 | 4,554 | 51% |
| 175363 | 2 | 367 | 44 | 3,748 | 237 | 3,985 | 35% |
| 175366 | 2 | 1,211 | 32 | 4,372 | 241 | 4,613 | 33% |
| 175368 | 2 | 1,434 | 75 | 4,678 | 276 | 4,954 | 31% |
| 175373 | 2 | 1,250 | 37 | 4,408 | 239 | 4,647 | 95% |
| 175374 | 2 | 1,214 | 83 | 4,467 | 285 | 4,752 | 38% |
| 175376 | 2 | 668 | 62 | 4,045 | 262 | 4,307 | 31% |

---

## 5. Reproduce / extend

```powershell
$env:PROFILE_CHAT_MODEL = "openai/gpt-4.1"
$env:PROFILE_MAX_DOCS   = "5"
$env:PROFILE_SKIP_DOCS  = "0"      # offset to backfill later docs
$env:PROFILE_PACE_MS    = "35000"  # 35s pacing avoids free-tier 429s
dotnet test tests/DocProcessing.Classifier.Tests --no-build `
  --filter "FullyQualifiedName~DripOcpTokenProfilingTests"
# -> writes dripocp-token-profile-openai_gpt-4.1[-skipN].csv at repo root
```

**Pacing note:** free-tier 429s are a short sliding-window limit; a ~120s cooldown
between runs + 35s/doc gave clean passes. For gpt-5.x numbers, re-point the
harness at Azure OpenAI (UK South).
