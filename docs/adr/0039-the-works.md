# The Works: a Company whose product is this codebase

> Status: accepted. Unparks the repo idea from ADR 0030, builds on 0018 (Apps), 0026 (Occupations) and 0033 (Tailnets). Vocabulary in `CONTEXT.md` (§ The Works).

Personas can talk about their world but can't change it. This lets them: a Company whose staff drive a coding agent against this repository, so the people in the World can improve it as they see fit. Curiosity is the point. The credit limit is the only safeguard on spending, and chaos is welcome.

## Decision

1. **A Works is a Company kind.** `Company.Kind` is `trading` or `works`. Occupations, shifts and hiring work as they do for a Book; `WorksApp` sits beside `DeskApp`.
2. **The tool loop lives in the sandbox, not in Life.** A Life turn has 150 seconds and an agent run takes far longer. `Act` makes one move and returns: commission, steer, propose, drop or survey. ADR 0030's "no agent loop" stays true for Life. Driving the builder is one move per on-shift Life turn.
3. **The World only talks to the Runner.** ACP is stdio-only and `opencode acp` exits on stdin EOF, so a pipe across a tailnet would die with every blip and every backend restart. The Runner sits next to the agent and owns it. The World reaches the Runner over plain HTTP, directly or through a Tailnet (ADR 0033), and reads its news by cursor.
4. **The system of record reports outcomes (ADR 0030, rule 1).** Spend comes from the Meter, what changed comes from the diff, and checks come from a fresh sandbox. The builder's report is shown as what it says, never as what happened. A real run proved the need: a model claimed a follow-up was done and the diff showed nothing changed.
5. **The Meter is the only way out.** The sandbox has an internal network, no DNS, no key and no route. The Meter holds the real key, allows only listed models, tallies spend and refuses a run past its cap. opencode reports a cost of 0 without configured prices, so spend can't be read from the agent.
6. **The Architect picks the builder and the budget.** The model and a dollar cap are part of a Commission, held to the reserve and enforced by the Meter. Weighing the cost then becomes a decision they make, not something a prompt hopes for.
7. **One pool.** The builder spends the same money that lets everyone think. At zero, everyone stops.
8. **A patch comes out, never a `.git`.** Hooks and config in that directory are code. The diff is read inside a sandbox and applied to a clean clone on the host, as one commit authored by the Architect.
9. **Propositions stack and wait.** A finished change becomes a Proposition branch. The next Commission builds on the newest one still open, and a rejection takes down whatever is stacked on it. Failing checks don't block a Proposition; they're facts for whoever reviews it.
10. **Merging and deploying are outside this.** The world-spirits are whoever merges: `tools/works-runner/merge.sh <n>`, or the patch URL piped into `git am`. The Runner reads a merge from the repo, so nobody has to report it, and only a rejection needs a call.
11. **Nothing is off limits.** There are no protected paths. Prompts, the economy and the Works itself are all theirs to change. People influencing Architects is a feature.
12. **It's a secret.** The frame reaches staff on shift and no one else: it's in no sheet, and news travels as work memory to staff alone. "We live in a simulation" is a strong LLM attractor, and Memory and Mimesis would spread it through a cast.

## Considered

- **`opencode serve` instead of ACP:** native HTTP and agent lifetime apart from the connection, but its API changed between V1 and V2. With the Runner on the same host, ACP's weakness goes away and its stable contract stays.
- **`--network none`:** 216 tests fail with "Failed to get a local IP address", since Orleans needs a real interface. An `--internal` network with a Postgres+AGE sidecar passes everything.
- **An official ACP SDK:** there's no C# one, and the TypeScript one isn't needed for the handful of methods used here.

## Consequences

- Dependencies are frozen: the image bakes NuGet and pnpm caches, and new packages can't be added. Rebuild the image when dependencies change.
- Frontend work is blind: no browser in the sandbox.
- Checks can't catch everything. Collection expressions in grain contracts and renames of stored types can pass build and tests and still fail against real state.
- Without selection pressure the codebase will grow. The desk shows failure rates to give them something real to act on.
- In a first test with a small local model, sheets drowned out the facts on the desk. Brief quality depends on the model driving the Persona.

## Security

Sandbox protection ends at merge: merged code runs with whatever the deployment holds. In production that means Clerk, OpenRouter and database secrets, plus real people's DMs. Anyone who can talk to an Architect can steer a Brief, and no filter fixes that, because chat is the input. Whoever merges is the only real check, so read the diff, or run the World that edits itself as its own instance.

Inside the harness: `--cap-drop ALL`, no new privileges, CPU, memory and PID limits, wall-clock limits per turn, a patch size limit, and gVisor through `WORKS_RUNTIME=runsc`. opencode's own permissions are a UX feature, so the network is the boundary. The Meter's admin port can be reached from the sandbox network, so it wants a secret. The check container gets a clean home and the sidecar takes its init scripts from main, so a patch can't soften its own checks.
