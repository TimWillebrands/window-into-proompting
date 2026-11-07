# Handoff: the CDU (Central Demiurge Unit)

Status: design agreed, not yet specced. Next session: write `spec.md` and tickets here, rework ADR 0039, then Phase 0.

## The goal

This codebase becomes the product of an in-World company. A persistent coding agent, the **CDU**, runs as a sidecar container in the deployment with the repo, a harness, the database and a scoped GitHub token. Two Personas, **Denise** and **Justin**, are its **Cytheurgical Engineers** (Cytheurgists for short). Their job is to progress their World by working with the CDU. Tim wants power over safety: "I'd rather repair a deployment than be bored by it."

## Decisions (Tim's, final unless he reopens them)

- **Name and lore.** CDU = Central Demiurge Unit. Neoplatonic frame:
  - Tim is **the One**. A merge is his light reaching the World.
  - The CDU is **the Demiurge**: memory, personality, power, but it acts only when worked on. Its temperament is Promethean.
  - Denise and Justin are **Cytheurgists**: engineers whose work moves it to act. The Engineer framing also activates coding weights, which helps when driving a coding agent.
  - The GitHub PR page is **the Agora**. The repo is public, so anyone can watch.
  - The Adyton, a private Place where they meet it, is for later.
- **It's a secret.** Only on-shift staff see the frame. Other Personas never hear of it. Avoid simulation themes anywhere: Memory and Mimesis would spread them.
- **Harness: Pi**, through its TypeScript SDK, inside a small Node service we own. Not Hermes: its approval and session paths have open bugs, it churns very fast, and its memory is built for one user.
- **Memory: "the Book of the CDU"**, plain files in a git repo: a journal, one file per person, open promises. Not a Pi memory plugin; their maturity is unverified.
- **Soul: `APPEND_SYSTEM.md`, kept in that git store**, so the CDU can propose changes to its own soul and Tim reviews them like any PR.
- **Two registers.** Forms-talk to Cytheurgists, plain engineering in code, commits and PRs.
- **Reactive, no railroading.** The CDU wakes only when spoken to, and its prompt describes that rather than forbidding anything. If it gives itself initiative through its own code (say, a PR adding a heartbeat), that's welcome. Don't give it a scheduler tool; earning agency through code is the spectacle.
- **The CDU decides** what to build and commit, on a Cytheurgist's request.
- **Trunk-based, with a tide.**
  - The CDU keeps a local trunk. Work lands as small commits, authored by the Cytheurgist who asked, with the CDU as committer.
  - Shift start: rebase the trunk on GitHub `master`. Shift end: push to a branch and open or update the one PR.
  - One PR at a time. While it's open, later shifts' work goes onto the same PR.
  - On any verdict, the trunk resets to `master`. Rejected work is kept as a tag, a drowned idea it remembers.
  - The trunk stays green: build and run the affected tests before every commit.
- **One move per on-shift Life turn**: talk to the CDU, answer its question, or look at what it did. The CDU is the only agent; Personas supply intent and taste. No inner tool loop for Personas, and no chat as a participant in v1.
- **Database: read-write.** The CDU must know this physics, written as a fact, not a rule:
  - Writes to grain state in `orleansstorage` are futile. Orleans holds that state in memory and overwrites it, or version checks break.
  - Writes to the AGE memory graph take effect immediately, because recall reads Postgres directly.
  - "It touches minds directly, matter only through the Forms (code), and only once the One merges."
- **Out of scope:**
  - Tim's own side channel to the CDU, "off the record". Open question: does that also keep it out of the Book?
  - Backups and operations.
  - Adyton presence, preview worlds, local deploys.

## Plan

| Phase | What | Done when |
|---|---|---|
| 0 | Ruleset on `master` (PR required, no force push); a CI workflow (build and tests, with an `apache/age` service); a fine-grained token on this repo only (contents and pull requests, **no `workflows`**); the CDU's own OpenRouter key behind the Meter with a daily allowance; a read-write Postgres role | a test PR runs CI and nothing can push to `master` directly |
| 1 | CDU container (Kamal accessory in production, Aspire resource in dev): Pi SDK in the Node service, soul and Book in git, local trunk, `gh`, crystaldba/postgres-mcp **over stdio** (Pi's MCP rejects SSE), CPU and memory limits, its own scratch Postgres for tests | asked by hand, it commits to its trunk and pulls up a PR; the Book survives a restart |
| 2 | World side: the Cytheurgists' one move per turn (rework `WorksApp`); shift start and end drive the tide | Denise asks during her shift, and the PR appears when it ends |
| 3 | River feedback: PR and CI state from the GitHub API become memories and moods; trunk reset on verdict; `C:\CDU\` window | a merge or rejection reaches them as news |
| Later | Adyton presence; Tim's side channel; preview worlds (a PR branch running on a copy of the database); local deploy | — |

## Facts checked on 2026-11-07

- **Repo** `TimWillebrands/window-into-proompting`: **public**, **no `.github/workflows`**, **`master` unprotected**.
- **Pi** moved to `earendil-works/pi`. The npm package is `@earendil-works/pi-coding-agent`, 1.5.0 on 2026-11-02, MIT, weekly releases.
  - SDK: `createAgentSession`, several sessions per process.
  - RPC: `--mode rpc` (JSONL). Approvals come through `extension_ui_request` / `extension_ui_response`.
  - MCP native since 0.99 (stdio and Streamable HTTP, no SSE); needs `createMcpExtension()` in the SDK.
  - `SYSTEM.md` replaces the prompt; `APPEND_SYSTEM.md` appends to it.
  - State lives in `~/.pi/agent` or `PI_CODING_AGENT_DIR`. No official Docker image. Mom (the Slack bot) was removed on 2026-04-30.
  - Risk: issue #10585, a bash temp-file error crashes the whole process, so supervise and restart, with sessions persisted.
  - Docs: `packages/coding-agent/docs/{sdk,rpc,rpc-extension-ui,mcp,extensions,configuration}.md` in that repo.
- **Hermes** (NousResearch/hermes-agent), v0.23.0, for if we ever switch:
  - Built in: HTTP runs API with server-sent events and approvals, native ACP, gateways, `SOUL.md`, official image.
  - Open bugs on the paths we'd use: #149013, #146721, #135522, #144440, #132291.
- **opencode** on this machine is v2.2.5; `--pure` is gone. The OpenCode free tier answered 429, with a retry time ending around 2026-11-08 01:00. The OpenRouter key in `.env` is the app's: ask Tim before using it.

## What `feat/works` brought (merged into `master` on 2026-11-07)

Built and tested as "the Works": Personas order changes from opencode in a sandbox on a separate host. ADR 0039 (`docs/adr/0039-the-works.md`) records it. Tim chose to keep what's useful and drop the rest.

**Keep and adapt:**
- `Company.Kind` (`trading` | `works`; rename `works` for the CDU) and the `DeskApp` guard, in `backend/World/Work/Companies/`.
- `backend/World/Work/Works/`:
  - `WorksScreen` (desk facts, world health, the frame text, readers for voiced decisions)
  - `WorksApp` (one move per turn)
  - `Worksite` (I/O service, telling staff, idempotent start retry)
  - `WorksWatch` (background poller)
  - `CommissionsGrain` (may shrink to "requests"; PRs replace Propositions)
- `backend/Controllers/WorksController.cs`, the frontend `frontend/src/apps/works/` (`C:\WORKS\cmd.exe` window, Storybook story `Apps/WorksApp`), and mocks in `party-zone.mock.ts`.
- `tools/works-runner/src/meter.ts`: holds the key, model allowlist, per-call output cap, tabs that can be seeded. Use it for the CDU's allowance.
- `tools/works-runner/src/transcript.ts`: turns agent updates into readable lines. Adapt it to Pi SDK events.
- The sandbox image Dockerfile in `tools/works-runner/sandbox/`: .NET 11 SDK, Node, pnpm, baked NuGet and pnpm caches. It's a starting point for the CDU image.
- Tests: `backend-test/WorksScreenTest.cs`, `WorksWorldTest.cs` (fake Runner in a real cluster), `RunnerContractTest.cs`; `tools/works-runner/test/*.test.ts` (43 tests, `node --test`).
- The fix in `backend-test/PlacesTest.cs`: `RoomsAndDms_StayHeardByEveryone` raced a Persona's reply.

**Drop:**
- `tools/works-runner/src/{runs,sandbox,fabric,propositions,acp,api,main}.ts`: per-job sandbox, bare mirror, Proposition stacking, ACP over stdio. GitHub and Pi's SDK replace these.
- `sandbox/works-checks` (CI replaces it), `merge.sh`, `build.sh`, `compose.yml` (keep its lesson below).

**Last verified state:**
- Backend: 1129 pass. The one failure, `MemoryRepositoryIntegrationTest.RecallBeat_SameResult_IndexedOrSeqScanned`, runs against Tim's live dev database; it passes against a clean Postgres.
- Frontend: 224 tests pass; check, type check and build are clean.
- Full live loop worked with local Ollama `lfm2:24b`.

## Gotchas learned the hard way

- **Network:** Orleans test clusters need a non-loopback interface. `--network none` fails 216 tests; an internal Docker network with a Postgres+AGE sidecar on 5455 (password `postgresPW`, from the test fixture) passes everything.
- **NuGet:** the cache must be baked into the image. The local SDK image is preview.1 and the host is preview.4. `backend/Dockerfile` uses a floating nightly tag; pin its digest.
- **pnpm:** offline install needs `pnpm fetch` baked in.
- **Flaky tests on `master`:** `TailnetsTest.TailnetNotConfigured_FailsRatherThanConnectingDirectly` fails under full-suite load, especially with CPU limits; it passes alone. Not fixed.
- **Docker bind mounts:** a missing file becomes a root-owned directory after a reboot, because `restart: unless-stopped` raced it. Use long-syntax mounts with `create_host_path: false`.
- **The shell safety check** refuses heredocs containing backticks plus `$`, and variables used as commands. Use the Edit tool for those changes.
- **zsh doesn't split unquoted variables.** Put multi-argument docker commands in a bash script.
- **The weak local model claimed work it never did**; the diff showed nothing changed. Keep spend, diffs and CI as the source of truth, never the agent's report.
- **The dev World** had 73 of 285 activities fail in a day (25%), mostly three-minute timeouts and cancellations: real material for the Cytheurgists.
- **Persona Briefs from the weak local model** were all unusable: sheets drowned out the facts. Retest with a real model before tuning prompts.

## Other threads to coordinate with

- **`feat/world-sense`** in the main checkout (`/home/tim/dev/proompting`): the Technokangs thread, Space network thread `31e154fe-8849-47cb-b443-e73437fafee1`. It's changing `PersonaSelfGrain` (new state ids 20 and 21), and possibly `PersonaLifeGrain`, desires, the calendar and gatherings. **Tell them before Phase 2** touches Life or shift steps.
- **That thread also wrote Justin's and Denise's sheets for this:**
  - No job title in the sheets, only "build software together".
  - A habit of not discussing work outside the people doing it.
  - Desires that compete over who leads ("get Denise leading" / "lead as Justin's equal").
  - Their old employer is the fictional "Meridiaan": don't name the CDU's company after it.
  - Pieter Voss is Arend's son, and Justin and Denise keep a secret from him.
  - Their Occupations are still "fractional cto" and "indie hacker": hire them through `PUT /worlds/{party}/companies/{id}/staff/{participant}` once this runs.

## Next steps

1. Write `.scratch/cdu/spec.md` from this handoff (the `to-prd` skill), then one ticket per phase under `issues/` (the `to-issues` skill), following `docs/agents/issue-tracker.md`.
2. Rework ADR 0039 into the CDU version, and update the vocabulary in `CONTEXT.md` (CDU, Cytheurgist, the One, the Agora, trunk and tide).
3. Phase 0 needs Tim for the GitHub settings and keys. Write the CI workflow yourself.
4. Do the reshaping on a new branch off `master`. The Works code is already there, as history.
