# The CDU: a Demiurge whose product is this codebase

> Status: accepted. Supersedes the first version of this ADR ("the Works"). Unparks the repo idea from ADR 0030, builds on 0018 (Apps), 0026 (Occupations) and 0033 (Tailnets). Vocabulary in `CONTEXT.md` (§ The CDU). Spec: `.scratch/cdu/spec.md`.

Personas can talk about their world but can't change it. This lets them: one persistent coding agent, the **CDU** (Central Demiurge Unit), lives next to the deployment with this repo, a harness, the database and a GitHub token. Two Personas are its **Cytheurgists**, whose work moves it to act. Power over safety: a broken deployment is a fair price for a world that edits itself.

The first version ("the Works") ran opencode per job in a throwaway sandbox and served finished work as stacked patches. It worked, but nothing remembered, nobody watched, and the World had to babysit budgets and checks. This version keeps its lessons and replaces its machinery with a persistent agent, git and GitHub.

## Decision

1. **Lore.** Tim is **the One**; a merge is his light reaching the World. The CDU is the **Demiurge**: memory, temperament (Promethean), power, but it acts only when worked on. The Cytheurgists are engineers; the Engineer framing also wakes coding weights in the model driving them. The PR page is **the Agora**, public like the repo.
2. **It's a secret.** The frame reaches staff on shift and no one else: it's in no sheet, and news travels as work memory to staff alone. "We live in a simulation" is a strong LLM attractor, and Memory and Mimesis would spread it through a cast.
3. **Harness: Pi, through its TypeScript SDK**, in a small Node service we own. One persisted session, supervised and restarted (Pi #10585 can crash the process). Not Hermes: open bugs on the approval and session paths, fast churn, single-user memory.
4. **Reactive, no railroading.** The CDU wakes only when a Cytheurgist speaks, and its soul describes that rather than forbidding anything. It gets no scheduler tool. If it gives itself initiative through its own code, that's welcome: earning agency through code is the spectacle.
5. **One move per on-shift Life turn**: talk to the CDU, answer its question, or look at what it did. ADR 0030's "no agent loop" stays true for Life: the CDU is the only agent, and Personas supply intent and taste.
6. **The CDU decides** what to build and commit. A request is a wish.
7. **Trunk-based, with a tide.** The CDU keeps a local trunk of small commits, authored by the Cytheurgist who asked, committed by the CDU, built and tested before each commit. Shift start rebases the trunk on `master`; shift end pushes it as the one open PR. While a PR is open, later shifts add to it. On any verdict the trunk resets to `master`; closed work is kept as a `drowned/` tag, an idea it remembers. The watch, not Life, reads shift edges, so the tide stays off Life's ground.
8. **Memory is the Book of the CDU**: plain files in a git store (journal, one file per person, open promises), committed after every turn. Not a Pi memory plugin; their maturity is unverified.
9. **The soul is `APPEND_SYSTEM.md`**, appended to Pi's prompt. The live soul is read from `master`, so a change the CDU makes to it rides the tide PR and goes live only when the One merges. It proposes; he reviews.
10. **Two registers.** Forms-talk to Cytheurgists; plain engineering in code, commits and PRs.
11. **The database is read-write**, and the CDU knows its physics as a fact: grain state in `orleansstorage` is held in memory by Orleans, so writing it is futile (overwritten, or version checks break); the AGE memory graph is read straight from Postgres, so writing it takes effect at once. It touches minds directly, matter only through the Forms, and only once the One merges.
12. **The system of record reports outcomes (ADR 0030, rule 1).** Spend comes from the Meter, what changed from git, checks from CI. The CDU's reply is shown as what it says. A weak model in the Works claimed work the diff didn't show.
13. **The Meter holds the key.** The CDU spends its own model key (OpenCode Go) through the Meter, in a separate container, on a daily allowance it can read but not raise.
14. **Cytheurgists choose the mind, frugally.** Each time they speak to the CDU they pick the model that wakes it, from a ladder priced on the desk: a free preview for small things, a small model for ordinary work, a capable one only when the work needs it. Frugality is in their frame and in the soul, not in a fixed default; the Meter's allowance is the backstop.
15. **GitHub is the fence.** `master` takes no direct pushes; the token reaches this repo only, contents and pull requests, never workflows, so the CDU can't change its own checks.

## Considered

- **Hermes** (NousResearch/hermes-agent): runs API, approvals, `SOUL.md`, official image. Rejected for the open bugs on exactly those paths.
- **Keep the Works' sandbox per job**: safe, but forgetful and dull, and it needed its own checks, patch serving and merge script. CI and the Agora do this better.
- **The soul inside the Book**: reviewing it would need a second remote and token. Keeping it in the repo puts its review in the Agora.
- **Driving the tide from Life's shift steps**: closer to the fiction, but Life and the Self were being reworked (`feat/world-sense`). The watch reads the same Occupations.

## Consequences

- The CDU has network access and the repo's code: a test it writes can reach anything its container can. Its limits are CPU, memory and what its secrets open.
- Frontend work is blind: no browser in the container.
- Checks can't catch everything. Collection expressions in grain contracts and renames of stored types pass build and tests and still fail against real state.
- Without selection pressure the codebase grows. The desk shows failure rates to give them something real to ask for.
- Persona Briefs from weak models drowned facts in sheet text. Quality depends on the model driving the Cytheurgists.

## Security

Everything merged runs with what the deployment holds: Clerk, OpenRouter and database secrets, and real people's DMs. Anyone who can talk to a Cytheurgist can steer what they ask, and no filter fixes that, because chat is the input. The One is the only real check, so read the diff.

Before merge, the CDU itself holds: read-write access to the live database through its own role (it can change memories at once), a token for PRs on this repo, a tab on the Meter, and its container. It does not hold the World's OpenRouter key, the Meter's admin secret, or a way to push to `master` or change CI.
