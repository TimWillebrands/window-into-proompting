# Works Runner

The sandbox side of the Works (ADR 0039). Runs on any Linux box with Node 22.18+ and Docker. No npm dependencies.

```
World (backend) --HTTP--> Runner --docker, ACP on stdio--> opencode, in a sandbox
                                                             |  internal network, no DNS, no key
                                                             v
                                                           Meter --> LLM upstream
```

Use a box you can afford to lose, not your daily machine. An agent that escapes the sandbox lands on the host.

## Set up

```bash
cp models.example.json models.json   # the builders an Architect can hire, with prices
./build.sh master                    # sandbox image from a clean export; rebuild when dependencies change

export WORKS_TOKEN=$(openssl rand -hex 24)        # what the World sends the Runner
export WORKS_METER_SECRET=$(openssl rand -hex 24) # what the Runner sends the Meter
export WORKS_UPSTREAM_URL=https://openrouter.ai/api/v1
export WORKS_UPSTREAM_KEY=...                     # stays in the Meter
export WORKS_BALANCE_URL=https://openrouter.ai/api/v1/key   # optional, shows the reserve on the desk
export WORKS_ORIGIN=git@github.com:you/repo.git   # the repo to work on; a local path works too
export WORKS_BRANCH=master
export WORKS_RUNTIME=runsc                        # gVisor; leave unset for plain runc

docker compose up -d   # the Meter and the internal network
npm start              # the Runner
```

Bind it to the tailnet with `WORKS_BIND=<tailscale ip>`, and allow one source on port 8787. Firewall the box's own egress too: tailnet rules don't touch it.

Other settings, in `src/config.ts`: `WORKS_PORT`, `WORKS_STATE`, `WORKS_MODELS`, `WORKS_CPUS`, `WORKS_MEMORY`, `WORKS_PIDS`, `WORKS_TURN_MINUTES`, `WORKS_IDLE_MINUTES`, `WORKS_CAP_TOKENS`, `WORKS_MAX_PATCH_BYTES`.

The upstream must speak OpenAI chat completions. Any `/v1` works, including a local Ollama at `http://host.docker.internal:11434/v1`.

## Connect a World

Set these on the backend, in user-secrets or the environment:

| Setting | Value |
|---|---|
| `Works__RunnerUrl` | `http://<box>:8787` |
| `Works__Token` | `WORKS_TOKEN` |
| `Works__Tailnet` | Optional: a Tailnet name from ADR 0033, when the backend can't reach the box directly |

Then, in Work & Wants, start a company of kind "works on the world" and hire Architects with shifts. The Works window (`C:\WORKS\cmd.exe`) shows the builder live.

## Merge a Proposition

Whoever merges is the world-spirits:

```bash
WORKS_TOKEN=... WORKS_RUNNER_URL=http://<box>:8787 tools/works-runner/merge.sh 3
```

Or from the Works window, which shows a `curl ... | git am` line. The commit keeps the Architect as author. Once it reaches `WORKS_BRANCH`, the Runner sees it and tells staff. To turn one down, use the window as an admin: `POST /worlds/{party}/works/commissions/{id}/reject`.

## Tests

```bash
npm test                                # Runner, no Docker needed
WORKS_LIVE_URL=http://127.0.0.1:8787 WORKS_LIVE_TOKEN=... dotnet test ../../backend-test --filter RunnerContractTest
```

The live test spends real tokens.
