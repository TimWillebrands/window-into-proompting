# Model selection for import-spearhead extraction (strict json_schema on OpenRouter)

**Date:** 2026-07-15
**Job:** `tools/bench/Probes/ImportSpearheadProbes.cs` — ~35 sequential calls/run, ~600 prompt + ~300 completion tokens/call, strict `response_format: {type: "json_schema", strict: true}`.
**Current model:** `tencent/hy3-20260706` (`:free` via Novita) — a reasoning MoE burning 83–97% of completion tokens on hidden reasoning (1.4k–4.5k reasoning tokens/call), 28–101 s/call, occasionally streaming zero content.

All model/pricing/provider facts below were pulled from the OpenRouter API on 2026-07-15:
`GET https://openrouter.ai/api/v1/models` (342 models; 263 advertise `structured_outputs`) and
`GET https://openrouter.ai/api/v1/models/{slug}/endpoints` for per-provider support.

---

## TLDR

- **Primary: `mistralai/mistral-small-3.2-24b-instruct`** — non-reasoning instruct model, `structured_outputs` supported on all 4 of its providers (DeepInfra/Parasail/Venice/Mistral), $0.10/M in / $0.30/M out → **~$0.005 per bench run**. No reasoning tokens at all, so latency is bounded by the ~300 content tokens.
- **Free fallback: `qwen/qwen3-next-80b-a3b-instruct:free`** — non-reasoning 80B-A3B instruct, `:free` endpoint (Venice) supports `structured_outputs` + `response_format`. Caveat: free tier is 20 req/min and 50 req/day without $10 credit purchase (1000/day with), so ~1 bench run/day on a credit-less key.
- **Zero-migration fallback:** keep `tencent/hy3` and send `reasoning: { enabled: false }` (or `effort: "none"`) — hy3 advertises configurable reasoning incl. a *disabled* mode, and its Novita `:free` endpoint lists the `reasoning` param. See [hy3 section](#zero-migration-fallback-cap-reasoning-on-tencenthy3).

Cost basis for "$/run": 35 calls × ~600 prompt (~21k) + ~300 completion (~10.5k) tokens.

---

## Comparison table

| Model | $/M in | $/M out | ~$/run | Structured outputs | Reasoning? | Ctx | Free variant | Notes |
|---|---|---|---|---|---|---|---|---|
| `mistralai/mistral-small-3.2-24b-instruct` | 0.10 | 0.30 | 0.005 | Yes — all 4 providers (`so=true`) | **No** | 131k | no | Primary pick |
| `qwen/qwen3-next-80b-a3b-instruct` | 0.10 | 1.10 | 0.014 | Yes — DeepInfra/Parasail/Google (Alibaba+Novita `response_format` only) | **No** | 262k | **yes** (Venice, `so=true`) | Free fallback |
| `google/gemini-2.5-flash-lite` | 0.10 | 0.40 | 0.006 | Yes — Google + AI Studio | Hybrid (thinking off by default on Lite; capped via `reasoning.max_tokens`) | 1M | no | Close second |
| `openai/gpt-5-nano` | 0.05 | 0.40 | ~0.006 | Yes — OpenAI + Azure | Yes (min via `reasoning.effort: "minimal"`; can't fully disable) | 400k | no | Best-in-class schema enforcement, small reasoning tax |
| `deepseek/deepseek-v4-flash` | 0.098 | 0.196 | 0.004+ | Mostly — 13/18 providers `so=true` (Novita/DeepSeek/GMICloud/SiliconFlow `response_format` only) | Hybrid (`reasoning: {enabled:false}`) | 1M | no | Cheapest large model, but reasoning-default risk |
| `inclusionai/ling-2.6-flash` | 0.010 | 0.030 | 0.0005 | Yes — Novita (`so=true`) | **No** | 262k | no | 10x cheaper than anything else; single provider, unproven quality |
| `mistralai/ministral-14b-2512` | 0.20 | 0.20 | 0.006 | Yes — Mistral + NextBit | **No** | 262k | no | Newer/smaller alternative to Mistral Small |
| *(current)* `tencent/hy3` | 0.20 | 0.80 | ~0.06 actual | Partial — DeepInfra/AtlasCloud `so=true` but **no `response_format`**; Novita `:free` `so=true` | Yes, **disable-able** | 262k | yes (Novita) | Reasoning burn dominates cost/latency today |

Sources: `https://openrouter.ai/api/v1/models` and `.../models/{slug}/endpoints`, fetched 2026-07-15. `~$/run` excludes reasoning tokens except where noted; hy3 "actual" reflects observed 1.4k–4.5k reasoning tokens/call.

---

## Per-candidate notes

### 1. `mistralai/mistral-small-3.2-24b-instruct` — primary
- Pure instruct model (`reasoning` **absent** from `supported_parameters`) — zero hidden-token risk, which is exactly the failure mode killing hy3.
- Endpoints (2026-07-15): DeepInfra ($0.075/$0.20), Parasail ($0.09/$0.30), Venice ($0.094/$0.25), Mistral ($0.10/$0.30) — **all** advertise both `structured_outputs` and `response_format`; uptime 99.8–100%.
- 24B dense, known-good instruction following for extraction/classification. Mistral's own endpoint gives first-party grammar-constrained decoding.
- ~$0.005/run; a 200-run benching campaign costs ~$1.

### 2. `qwen/qwen3-next-80b-a3b-instruct` (+ `:free`) — free fallback
- Explicit non-thinking variant (Qwen ships `-thinking` separately); `reasoning` not in supported params.
- `:free` endpoint is Venice with `structured_outputs: true` — one of very few free endpoints with real strict-schema support.
- Paid endpoints with `so=true`: DeepInfra ($0.09/$1.10), Parasail, Google. Alibaba and Novita only expose `response_format` — pin providers or set `provider: { require_parameters: true }`.
- Free-tier limits (OpenRouter docs, `https://openrouter.ai/docs/api-reference/limits`, 2026-07-15): **20 req/min, 50 req/day** without a lifetime $10 credit purchase; **1000/day** with. 35 sequential calls fit one run/day on a credit-less key.

### 3. `google/gemini-2.5-flash-lite`
- Lite tier ships with thinking **off by default** (unlike 2.5 Flash/Pro); model still lists `reasoning` param so you can pin `reasoning: { max_tokens: 0 }`/`enabled: false` defensively.
- Google + AI Studio endpoints all `so=true`; Google's structured-output implementation is mature. $0.10/$0.40.
- Newer `google/gemini-3.1-flash-lite` exists at $0.25/$1.50 — 2.5-flash-lite stays the price/perf pick for this tiny job.

### 4. `openai/gpt-5-nano`
- Cheapest OpenAI tier ($0.05/$0.40; newer `gpt-5.4-nano` costs 4x at $0.20/$1.25 — not worth it here).
- Always a reasoning model; `reasoning: { effort: "minimal" }` caps it (~10% of completion tokens per OpenRouter reasoning docs) but never fully disables. OpenAI's strict json_schema enforcement is the reference implementation, so this is the pick if any schema drift is observed with open-weights models.
- Endpoint uptime shown 96–98% (lower than the Mistral/Qwen endpoints on the day of capture).

### 5. `deepseek/deepseek-v4-flash`
- Extremely cheap ($0.098/$0.196) 1M-ctx hybrid released after v3.2; 18 endpoints, 13 with `so=true`.
- Hybrid reasoning defaults vary by provider; must send `reasoning: { enabled: false }` and pin `so=true` providers (e.g. `provider: { only: ["deepinfra", "baidu"] }` or `require_parameters: true`). More moving parts than the primary pick — good cost hedge, not the default.

### 6. `inclusionai/ling-2.6-flash`
- 104B-A7.4B instant/instruct model at $0.01/$0.03 — a full run costs $0.0005. Non-reasoning, `so=true` on its single (Novita) endpoint, 100% uptime shown, 32k max output.
- Single-provider dependency and no track record on this task; worth one comparative bench run, not the default.

### Rejected
- `tencent/hy3:free` as-is — current pain point; also its free Novita endpoint does **not** list `response_format` (only `structured_outputs`), so strict mode rests on one param.
- `google/gemma-4-26b-a4b-it:free` — free with `so=true` (Darkbloom), but Darkbloom uptime 96.4% and the model is reasoning-capable; qwen3-next-80b:free dominates it.
- `meta-llama/llama-4-maverick` — fine (non-reasoning, `so=true` on 5 providers) but 2.5x the price of Mistral Small 3.2 for no expected extraction-quality gain.
- `deepseek/deepseek-chat` ($0.20/$0.80) — non-reasoner, works, but strictly dominated on price by v4-flash and on simplicity by Mistral Small.

---

## Zero-migration fallback: cap reasoning on `tencent/hy3`

`tencent/hy3` (canonical `tencent/hy3-20260706` — same model the probe uses) advertises "a configurable reasoning effort" incl. a disabled mode (model description, models API 2026-07-15; the sibling `tencent/hy3-preview` description spells out "disabled, low, and high modes"). Both the paid and `:free` endpoints list `reasoning` and `reasoning_effort` in `supported_parameters`; the Novita `:free` endpoint has `structured_outputs: true`.

Per OpenRouter's reasoning docs (`https://openrouter.ai/docs/use-cases/reasoning-tokens`, fetched 2026-07-15), the unified control is a top-level `reasoning` object:

```jsonc
{
  "model": "tencent/hy3-20260706:free",
  "reasoning": { "enabled": false }
  // or: "reasoning": { "effort": "none" }   // effort enum: max|xhigh|high|medium|low|minimal|none
  // or cap instead of disable: "reasoning": { "max_tokens": 512 }
}
```

Legacy equivalent: `include_reasoning: false` maps to `reasoning: { exclude: true }` (hides but does **not** disable generation — don't use it for the token-burn problem). If Novita ignores the disable flag, `reasoning: { effort: "low" }` or a `max_tokens` cap still bounds the 4.5k-token blowups and the zero-content turns.

---

## Recommended request shape (primary pick)

```jsonc
{
  "model": "mistralai/mistral-small-3.2-24b-instruct",
  "response_format": {
    "type": "json_schema",
    "json_schema": { "name": "memory_items", "strict": true, "schema": { /* existing probe schema */ } }
  },
  "provider": { "require_parameters": true },  // route only to endpoints that support structured_outputs
  "temperature": 0
}
```

- `strict: true` inside `json_schema` is what turns on grammar-enforced decoding (OpenRouter structured-outputs docs, `https://openrouter.ai/docs/features/structured-outputs`, fetched 2026-07-15).
- Without `require_parameters`, OpenRouter may route to a provider lacking the feature and the request fails with a lack-of-support error (same docs page).
- Free fallback slug: `qwen/qwen3-next-80b-a3b-instruct:free` — same request shape, no `reasoning` key needed (non-thinking variant).

## Slugs quick reference

| Purpose | Slug |
|---|---|
| Primary | `mistralai/mistral-small-3.2-24b-instruct` |
| Free fallback | `qwen/qwen3-next-80b-a3b-instruct:free` |
| Schema-drift escape hatch | `openai/gpt-5-nano` + `reasoning: { effort: "minimal" }` |
| Cost-floor experiment | `inclusionai/ling-2.6-flash` |
| Zero-migration | `tencent/hy3-20260706` (or `:free`) + `reasoning: { enabled: false }` |
