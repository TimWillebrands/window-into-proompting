# Support greeting note — "Dear Valued User" audit

**Date:** 2026-11-14 — **Scope:** this repository, the Partytown grain store, the AGE memory graph.
**Purpose:** mark every place the words `Dear Valued User` appear in the support path. No removal, no rewording: the greeting is left exactly as found.

## Result: no support path exists in this repository, and no occurrence of the words was found anywhere in it

The audit therefore stops here, per instruction. For review, the full search coverage:

- **Code, all refs:** `git grep -i "Dear Valued"` over every branch and tag on the remote: `master`, `trunk`, `origin/world`, `origin/parallel-harness-runs` — no matches.
- **Support path:** no support tool, service, or app exists under `backend/`, `backend/World/Apps/`, `frontend/src/apps/`, or `tools/`. There is no file the greeting could be a part of.
- **Grain store (`orleansstorage`), every grain type, case-insensitive scan of every payload:** no `Dear Valued User` in any template, prompt, or state.
- **AGE graph `memory` (all labels, all string properties):** no node carries the words.

## Where the words *do* appear — quotations about the prototype, not instances of it

| Where | Author | Exact line |
| --- | --- | --- |
| Board thread `a9e00caf-728d-4e99-a57f-981fb8e9db8b` (title "Our support tool still greets customers with Dear Valued Use…") | Justin de Castelnaudary Ruysch | "Our support tool still greets customers with Dear Valued User. It is a small thing, but a customer notices it before they notice anything else, so I mean to have that template removed before the new support engineer starts on Monday." |
| Reply, same thread (id `774814ad-6071-436f-89f3-2869af7bb7a8`) | Anne de Castelnaudary | "…Dear Valued User is a cold jar of duck, mon amour." |
| Justin's diary | Justin | "The Dear Valued User template has to go before the new engineer starts, and that I will not wait for. The touch history on staging is the real question." |
| Group chat (message id 29) | Justin | "…The \"Dear Valued User\" thing is coming out before Monday, for what it's worth." |

Every one of these is a person *talking about* the greeting. Not one is the greeting itself.

## Open question for review

An external "staging" environment (with "touch history") is referenced in the diary as the origin. It is not reachable from this clone. If the template lives there, it is outside the audited surfaces above and needs access before it can be marked.
