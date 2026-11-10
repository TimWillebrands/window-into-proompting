# Support greeting note — "Dear Valued User" audit

**Date:** 2026-11-10 — **Scope:** this repository, the Partytown grain store, the AGE memory graph.
**Purpose:** mark every place the words `Dear Valued User` appear in the support path. No removal, no rewording: the greeting is left exactly as found.

## Result: the repository contains no support path and no instance of the greeting

Search coverage:

- **Code, all refs:** `git grep -i "Dear Valued"` over every branch and tag on the remote (`master`, `trunk`, `origin/world`, `origin/parallel-harness-runs`, drowned tags) — no matches.
- **Support path:** no support tool, service, or app exists under `backend/`, `backend/World/Apps/`, `frontend/src/apps/`, or `tools/`. There is no file the greeting could be part of.
- **Grain store (`orleansstorage`), every grain type, case-insensitive scan of every payload:** no template, prompt, or state carries the words.
- **AGE graph `memory` (all labels, all string properties):** no node carries the words.

## Where the words do appear — four lines, all people speaking about the greeting

Quoted as they stand.

**1. Board thread `a9e00caf-728d-4e99-a57f-981fb8e9db8b`, Justin de Castelnaudary Ruysch** — title:

> Our support tool still greets customers with Dear Valued Use…

body:

> Our support tool still greets customers with Dear Valued User. It is a small thing, but a customer notices it before they notice anything else, so I mean to have that template removed before the new support engineer starts on Monday.

**2. Reply in the same thread, id `774814ad-6071-436f-89f3-2869af7bb7a8`, Anne de Castelnaudary:**

> Justin, a customer notices the warmth of a greeting before anything else, you're right. So say hello to them properly yourself, not with a template. Dear Valued User is a cold jar of duck, mon amour.

**3. Justin's diary:**

> The Dear Valued User template has to go before the new engineer starts, and that I will not wait for. The touch history on staging is the real question. Denise is pulling it before Monday, and I want to read the record myself rather than take anyone's word, mine included.

**4. Group chat, message id 29, Justin:**

> The support queue, if I'm honest. Someone writes in with a real problem, waits days, and gets back a template that calls them valued. I'd want the first answer to be about their problem. The "Dear Valued User" thing is coming out before Monday, for what it's worth.

Every one of these is a person *talking about* the greeting. Not one is the greeting itself.

Further matches in the grain store after these four are the CDU's own records of this audit — its activity log entries and chat replies describing the search — not the greeting.

## Open question for review

An external "staging" environment (with "touch history") is referenced in the diary as the origin. It is not reachable from this clone. If the template lives there, it is outside the audited surfaces above and needs access before it can be marked.
