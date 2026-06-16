# DripOCP Token Cost — Foundry solution vs TTA solution

**Scope:** DripOCP package type only · **Normalisation:** 100,000 pages · **Date:** 2026-06-08

Like-for-like comparison of AI token cost for the **DripOCP** document type
between:

- **Foundry solution** — our DocProcessing pipeline. Azure Document Intelligence
  OCR → MAF agent on **gpt-4.1** that **classifies intent and extracts fields in a
  single pass** (KB-grounded; emits strict JSON). Figures are **measured actuals**
  from `DripOCP-Token-Profile-Results.md` (n=8 DripOCP docs), extrapolated to
  100,000 pages.
- **TTA solution** — their Phase-2 estimate (`TTA-Cost-Analysis.png`), modelled
  on OpenAI models with a **20% safety buffer**, also normalised to 100,000 pages.
  TTA splits the work into **two separate AI passes**: *Extraction* and
  *Classification*.

Both sides assume the **same DripOCP page profile: median 2 pages/document**, so
100,000 pages ≈ 50,000 documents in both models. This makes the comparison
genuinely like-for-like at the page level.

---

## 1. Function mapping (what counts as "like-for-like")

The Foundry agent performs, in **one** LLM pass: intent classification + field
extraction (the `payload`) + reject-rule evaluation. TTA performs the equivalent
work in **two** passes (Extraction + Classification). The honest comparison is
therefore **Foundry (1 pass) vs TTA (Extraction + Classification combined)**.

| | Foundry | TTA |
|---|---|---|
| OCR | Azure Document Intelligence (per-page, **0 tokens**) | Folded into LLM passes (OpenAI models) |
| Classify intent | ✅ same agent pass | "Classification" pass |
| Extract fields | ✅ same agent pass | "Extraction" pass |
| Vector search (KB grounding) | ✅ embeddings (text-embedding-3-small) | not modelled separately |
| Safety buffer | none (raw measured) | +20% |

---

## 2. Headline — total tokens for DripOCP @ 100,000 pages

| Solution | Tokens / page | **Total tokens** |
|---|---|---|
| **Foundry** (classify+extract, measured) | **2,302** | **230.2 M** |
| Foundry (+20% buffer, to match TTA method) | 2,762 | 276.2 M |
| TTA — Extraction pass | 815 | 81.5 M |
| TTA — Classification pass | 2,135 | 213.5 M |
| **TTA — combined (extract + classify)** | **2,950** | **295.0 M** |

**Result:**
- Foundry **230.2 M** vs TTA combined **295.0 M** → **Foundry ~22% lower** (raw).
- Even after adding TTA's own 20% buffer to our figures (276.2 M), **Foundry is
  ~6% lower** than TTA's buffered combined estimate.

---

## 3. Input vs output split (per page)

| | Input tok/page | Output tok/page | Notes |
|---|---|---|---|
| **Foundry** (measured) | **2,175** | **127** | 94.5% input / 5.5% output |
| TTA (stated rates) | 2,500 (cls 2,000 + ext 500) | 1,300 (cls 1,000 + ext 300) | 66% input / 34% output |

- **Output is where the gap is widest: 127 vs 1,300 tok/page — Foundry emits ~10×
  fewer output tokens.** The agent's system prompt forbids prose/markdown and
  returns compact strict JSON, whereas TTA budgets ~1,000 output tok/page for
  classification alone. Output tokens are typically the most expensive, so this
  drives most of the cost advantage.
- **Input** is also lower (2,175 vs 2,500 tok/page, ~13%), helped by KB-grounded
  retrieval keeping the prompt tight rather than stuffing full schemas per call.

---

## 4. The OCR architectural difference (not in the token totals)

The Foundry token totals above **exclude OCR**, because OCR runs on Azure
Document Intelligence and is billed **per page (100,000 pages), not per token**.
TTA's model appears to fold document understanding into LLM tokens (no separate
OCR line). So in pure token terms Foundry is lower **and** offloads OCR to a
cheaper per-page service — but a complete £/$ comparison must add the DI per-page
charge to the Foundry side. That is a pricing exercise, not a token exercise, and
is out of scope for this token-only comparison.

---

## 5. Per-document view (reference)

| | Foundry (gpt-4.1, measured, per doc) | TTA (implied, per 2-page doc) |
|---|---|---|
| LLM input | 4,297 | ~5,000 |
| LLM output | 254 | ~2,600 |
| Embeddings | 52 | — |
| **Total / doc** | **~4,603** | **~5,900** |

---

## 6. Caveats

- **Foundry = measured, TTA = estimated.** Our numbers are 8 real DripOCP docs on
  gpt-4.1 (tight distribution, sd ±6%); TTA's are modelled with a 20% buffer.
- **Model differs.** Foundry profiled on **gpt-4.1** (gpt-5.x is entitlement-blocked
  on the dev token). TTA states "OpenAI models" without a specific id. A future
  Foundry run on Azure OpenAI gpt-5.x would refine the comparison.
- **Page assumption shared.** Both use 2 pages/doc for DripOCP; if production
  DripOCP averages a different page count, scale both sides by the same factor —
  the *ratio* (Foundry ~22% lower) is preserved.
- **OCR excluded** from token totals on the Foundry side (per-page DI billing) —
  see §4.

---

### Bottom line
For DripOCP at 100,000 pages, the **Foundry solution uses ~230 M tokens vs the
TTA solution's ~295 M — about 22% fewer (≈6% fewer even after matching TTA's 20%
buffer)**, driven mainly by a ~10× leaner output-token footprint from strict-JSON,
single-pass classification+extraction, plus OCR offloaded to per-page Document
Intelligence instead of LLM tokens.
