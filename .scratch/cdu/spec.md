# The CDU: a Demiurge whose product is this codebase

Status: ready-for-agent

Design source: `handoff.md` in this folder (Tim's decisions, final). Replaces the Works' Runner, sandbox and Propositions (ADR 0039, reworked).

## Problem Statement

Personas can talk about their World but can't change it. The Works (ADR 0039) tried: Architects ordered Commissions from opencode in a throwaway sandbox, and finished work stacked up as Propositions served as patches. It worked once with a weak local model, but it was built for safety rather than spectacle: every job forgot everything, the builder had no personality, merges ran through a shell script nobody watches, and the World had to babysit Commissions, budgets and checks.

Tim wants power over safety ("I'd rather repair a deployment than be bored by it"): one persistent coding agent that remembers, has a temperament, works on real GitHub pull requests anyone can watch, and only acts when the people whose job it is move it to.

## Solution

A sidecar container, the **CDU** (Central Demiurge Unit), lives next to the backend with this repo, the Pi coding agent, the database and a scoped GitHub token. Two Personas, Denise and Justin, are its **Cytheurgists**. On shift, each Life turn they make one move: talk to the CDU, answer its question, or look at what it did.

The CDU keeps a **trunk** of small green commits, authored by whoever asked. The **tide** moves with shifts: at shift start the trunk rebases on GitHub `master`; at shift end it goes out as the one open PR, the **Agora**. Tim, **the One**, merges or closes it. That verdict flows back as news, moods and memories, and the trunk resets to `master`. Rejected work stays as a tag, a drowned idea.

The CDU remembers through **the Book**: plain files in a git store (journal, one file per person, open promises). Its **soul** is an `APPEND_SYSTEM.md` it can propose changes to, reviewed by Tim like any other change.

Only staff on shift know any of this. No one else hears of it.

## User Stories

### Tim (the One)

1. As the One, I want the CDU's work to arrive as one GitHub PR, so that I review it where I review everything else.
2. As the One, I want `master` protected by a ruleset (PR required, no force push), so that nothing reaches the deployment without my merge.
3. As the One, I want every PR built and tested in CI with a Postgres+AGE service, so that I see red or green before reading a diff.
4. As the One, I want the CDU's GitHub token limited to this repo, contents and pull requests, without `workflows`, so that it can't change CI or touch other repos.
5. As the One, I want the CDU to spend from its own OpenRouter key through the Meter with a daily allowance, so that a bad day costs a bounded amount and never drains the World's key.
6. As the One, I want the allowance to survive a Meter restart, so that restarting doesn't refill it.
7. As the One, I want commits authored by the Cytheurgist who asked and committed by the CDU, so that `git log` tells me who wanted what.
8. As the One, I want the PR body to list each commit with who asked for it, so that I can review intent as well as code.
9. As the One, I want closing a PR to reset the trunk to `master` and tag the rejected work, so that nothing I turned down creeps back in, and the CDU can still remember it.
10. As the One, I want soul changes to reach me as part of the PR, live only once I merge, so that the CDU can't rewrite itself unreviewed.
11. As the One, I want the CDU to have read-write database access with a dedicated role, so that it can touch minds (the memory graph) directly.
12. As the One, I want the CDU container limited in CPU and memory, so that a runaway test run doesn't starve the World.
13. As the One, I want the CDU supervised and restarted on a crash, with its Pi session and Book on a volume, so that a crash (Pi issue #10585) costs a turn, not its memory.
14. As the One, I want to talk to the CDU by hand over its API in development, so that Phase 1 is testable without the World.
15. As the One, I want the CDU in the Aspire AppHost in dev and as a Kamal accessory in production, so that it runs the same way as everything else.

### Cytheurgists (Denise, Justin)

16. As a Cytheurgist on shift, I want to see the CDU on my desk (asleep, working, what it last said and to whom), so that I know whether to talk to it.
17. As a Cytheurgist, I want to tell the CDU what I want for our World in my own words, so that it can decide what to build.
18. As a Cytheurgist, I want to answer the CDU when it asked me something, so that work isn't stuck on a question.
19. As a Cytheurgist, I want to look at what the CDU did (commits on the trunk, the open PR, its checks) and remember it, so that I know what's true rather than what it claimed.
20. As a Cytheurgist, I want the CDU's reply to reach me as a memory, so that I carry the conversation into my next turns.
21. As a Cytheurgist, I want the One's verdict on a PR to reach me as news with a mood, so that a merge feels like a win and a rejection like a loss.
22. As a Cytheurgist, I want CI failing on our PR to reach me, so that I can ask the CDU to fix it.
23. As a Cytheurgist, I want to see the world's health on my desk (activities that failed in the last day), so that I have something real to ask for.
24. As a Cytheurgist, I want to see how much of the CDU's daily allowance is left, so that I weigh what I ask for.
25. As a Cytheurgist, I want the frame of my work in my desk text and nowhere else, so that I keep the secret without having to remember to.
26. As a Cytheurgist off shift, I want only a vague glance at the bench, so that work stays at work.

### The CDU

27. As the CDU, I want to wake only when a Cytheurgist speaks to me, and know that this is how I am, so that I don't act unbidden (and can earn initiative through my own code).
28. As the CDU, I want to know who is speaking to me, so that I can keep a file on each of them in my Book.
29. As the CDU, I want my Book (journal, people, promises) to persist and be versioned, so that I remember across turns, restarts and tides.
30. As the CDU, I want to decide what to build and commit, so that a request is a wish, not an order.
31. As the CDU, I want to build and run the affected tests before every commit, so that the trunk stays green.
32. As the CDU, I want to know the physics of the database (grain state writes are futile, memory graph writes are immediate), so that I touch minds directly and matter only through the Forms.
33. As the CDU, I want to speak in forms-talk to Cytheurgists and plain engineering in code, commits and PRs, so that each audience gets its register.
34. As the CDU, I want news of verdicts, rebase trouble and CI to be told to me at my next waking, so that I know what happened while I slept.
35. As the CDU, I want rebase conflicts left for me to resolve, so that the tide doesn't silently drop my work.
36. As the CDU, I want a scratch Postgres for tests, so that tests never touch the live database.
37. As the CDU, I want `gh` with my token, so that I can read the Agora and comment on my own PR.
38. As the CDU, I want to see what my own spend is today, so that I can pace myself.

### Spectators

39. As a spectator, I want the `C:\CDU\` window to show the CDU's live transcript, the trunk and the PR link, so that I can watch the World edit itself.
40. As a spectator, I want the Agora (the PR page) to be public, so that anyone can follow along.

### The World and other Personas

41. As a Persona who isn't on shift staff, I never hear of the CDU, so that "we live in a simulation" doesn't spread through Memory and Mimesis.
42. As the World, I want shift start and end of the Cytheurgists to drive the tide, so that the rhythm of work sets the rhythm of PRs.
43. As the World, I want an unreachable CDU to make a quiet desk, not a broken Life turn, so that a crashed sidecar never hurts anyone's day.

## Implementation Decisions

### Shape

- Two new runtime pieces: the **CDU service** (Node, embeds Pi through its TypeScript SDK) and the **Meter** (reused from the Works, extended with daily allowances). They run as separate containers so the real OpenRouter key never sits where the agent can read it.
- The World talks only to the CDU service, over HTTP with a bearer token, like the Runner before it (directly, or through a Tailnet per ADR 0033).
- What goes: the per-job sandbox, the bare mirror, Proposition stacking, ACP over stdio, `works-checks`, `merge.sh`, `build.sh`, `compose.yml`. GitHub and the Pi SDK replace them. The Meter and the transcript adapter move into the CDU package.

### The CDU service (deep module: `cdu` package)

One process, one Pi `AgentSession`, persisted under a Pi agent dir on a volume. Its HTTP surface:

| Call | What |
|---|---|
| `GET /status` | awake/working, queue length, model, trunk (head, commits ahead of `master`), the open PR (number, url, state, checks), allowance (spent, daily), news cursor |
| `POST /talk` `{from: {id, name}, text, kind: talk\|answer}` | queues a turn and returns its id; turns run one at a time |
| `GET /turns/{id}` | status, what the CDU said back, whether it asked something, commits it made |
| `GET /lines?after=N` | transcript lines by cursor (`heard`, `say`, `thought`, `tool`, `failed`, `status`) |
| `GET /news?after=N` | facts for the World: `replied`, `committed`, `pr-opened`, `merged`, `closed`, `checks`, `rebased`, `conflict` |
| `POST /tide/in` | shift start: fetch, reset if a verdict came, else rebase the trunk on `origin/master` |
| `POST /tide/out` | shift end: if the trunk is ahead, push to the tide branch and open or update the one PR |

- **Turns.** A turn sets `GIT_AUTHOR_NAME`/`EMAIL` to the speaker (committer is the CDU), prefixes the message with who speaks and any news since the last waking, prompts the session, and records the reply when Pi settles. "Asked something" is read from the reply (a question in its last paragraph), not trusted from a flag.
- **The Book** is a git repo on the CDU's volume: `journal.md`, `people/<slug>.md`, `promises.md`, seeded on first start. The CDU writes it with its own tools; the service commits whatever changed after every turn, and writes factual lines (verdicts) to the journal itself.
- **The soul** is `APPEND_SYSTEM.md`, appended to Pi's system prompt. The live copy is the one on `origin/master`, read at every tide-in, so a change the CDU makes rides the tide PR and goes live only when the One merges. The seed lives in the repo with the CDU package. (Refines the handoff's "kept in that git store": the Book holds memory; the soul is versioned in the repo so review happens in the Agora.)
- **The tide** is pure git plus `gh`. One PR at a time from a fixed branch. A verdict (merged or closed) is noticed by polling `gh pr view`; closed-unmerged work is tagged `drowned/<date>-pr<n>` before the reset. Resets wait until no turn is running. A rebase conflict aborts, keeps the trunk, and becomes news for the CDU's next waking.
- **Model access**: Pi gets one custom provider pointing at the Meter (OpenAI completions API), with a tab token as its key and the Meter's model list as its models.
- **MCP**: crystaldba `postgres-mcp` over stdio (Pi rejects SSE) in unrestricted mode, connected with the `cdu` role.
- **Tests run against a scratch Postgres+AGE** container, reached through `PARTYTOWN_TEST_CONN`.
- **Supervision**: the container restarts the service on exit; the service resumes the most recent Pi session on start.

### The Meter

- Tabs can be **daily**: a USD allowance that resets at UTC midnight. Tabs can be declared in config, so the CDU's tab exists without an admin call.
- Tab state persists to a file, so a restart doesn't refill an allowance.
- A tab holder can read its own tab (`GET /v1/tab` with its token); raising a cap stays behind the admin secret, which the CDU container doesn't have.

### The World side

- `Company.Kind` gains `cdu` (legacy `works` reads as `cdu`). The `DeskApp` guard stays.
- **`CduApp`** (rework of `WorksApp`), one move per on-shift Life turn:
  - `talk`: voice what they decided as one message to the CDU, in their own words.
  - `answer`: offered only when the CDU's last reply to anyone asked something and hasn't been answered.
  - `look`: reads the trunk, PR and checks into the Persona's work memory. No call to the CDU beyond a status read.
- **`CduScreen`** (rework of `WorksScreen`): desk facts, world health (kept), allowance, the frame text for Cytheurgists, and readers for voiced moves. Pure, tested without a cluster.
- **`CduGrain`** (shrinks `CommissionsGrain`), per Party: recent exchanges (who said what, the reply, whether it asked), event and news cursors, and who was on shift at the last beat. Pure state; I/O stays in the site service.
- **`Cytheurgy`** (rework of `Worksite`): the I/O around the grain. Tells staff as work memory only, never the wider World.
- **`CduWatch`** (rework of `WorksWatch`): polls `/events` and `/news`; derives shift start and end from staff Occupations, beat to beat, and calls `tide/in` and `tide/out`. This keeps the tide out of Life and shift steps.
- **River feedback**: `replied` reaches the speaker; `merged`, `closed` and failing `checks` reach all staff as work memory, verdicts with a mood (merge lifts, rejection sinks), as the Works did.
- **API**: `GET /worlds/{party}/cdu` (overview) and `GET /worlds/{party}/cdu/lines` for spectators, anonymous GETs. No reject endpoint: the One rejects on GitHub.
- **Frontend**: `C:\CDU\` window (rework of `C:\WORKS\cmd.exe`): live transcript, exchanges, trunk and PR link; Storybook story and mocks updated.

### Frame and secrecy

- The Cytheurgists' frame (Neoplatonic, Engineer framing) reaches staff on shift only: it's in no sheet. Off shift they see one plain line.
- News travels as work memory to staff alone. The CDU never talks to anyone else; Tim's side channel is later.
- Avoid simulation themes everywhere outside the frame.

### Phase 0 (Tim's part)

- Ruleset on `master` (PR required, no force push), the fine-grained token, the CDU's OpenRouter key, the `cdu` database role password. The CI workflow and the role script are written by the agent.

## Testing Decisions

- Test external behaviour: what the HTTP surface answers, what git looks like after a tide, what a Persona sees and which move it gets. Not how a function is split.
- **CDU package** (Node, `node --test`): the Meter (prior art: the Works' meter tests, extended for daily tabs, persistence and self-read); the transcript adapter fed Pi events; the tide against real local git repos in a temp dir (an origin, a clone, a fake `gh`); turn bookkeeping (authorship, reply, question detection) with a fake session.
- **Backend** (xUnit): `CduScreenTest` (prior art `WorksScreenTest`) for rendering, affordances and readers; `CduWorldTest` (prior art `WorksWorldTest`: a fake CDU in a real test cluster) for one move per turn, replies reaching the speaker, verdicts reaching staff, shift edges calling the tide; a contract test against a live CDU, skipped unless configured (prior art `RunnerContractTest`).
- **Frontend** (Vitest): the window's pure helpers (prior art `works-lib.test.ts`) and the Storybook story.
- CI runs all of the above on every PR.

## Out of Scope

- Tim's side channel to the CDU ("off the record"), and whether it stays out of the Book.
- Backups and operations of the CDU's volume.
- The Adyton (a private Place where they meet it), preview worlds, local deploys.
- Chat with the CDU as a Room participant, and inner tool loops for Personas.
- A scheduler or heartbeat tool for the CDU: initiative is for it to earn through its own code.

## Further Notes

- The weak local model claimed work it never did. Spend, diffs and CI are the source of truth; the CDU's reply is shown as what it says.
- `TailnetsTest.TailnetNotConfigured_FailsRatherThanConnectingDirectly` flakes under full-suite load and may flake in CI.
- `feat/world-sense` (merged) reworked `PersonaSelfGrain` and Life. The tide is driven from the watch, not Life, to stay off that ground. Denise's and Justin's sheets came from that thread: hire them as staff through `PUT /worlds/{party}/companies/{id}/staff/{participant}` once this runs. Don't name the Company after "Meridiaan".
