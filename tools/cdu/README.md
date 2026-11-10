# The CDU

The Central Demiurge Unit (ADR 0039): Pi, through its SDK, in a small Node service, with this repo, `gh`, the database and a daily allowance.

```
World (backend) --HTTP--> CDU (Pi session, trunk, Book) --> Meter --> OpenCode Go (or OpenRouter)
                            |  gh, git push         |  postgres-mcp (stdio)
                            v                       v
                          GitHub (the Agora)      live Postgres (role cdu)
                            scratch Postgres+AGE for its tests
```

## Pieces

| | |
|---|---|
| `src/cdu.ts` | One queue for turns and the tide; authorship, replies, news, notes for its next waking |
| `src/tide.ts` | The trunk: rebase in, push out as one PR, reset and `drowned/` tag on a verdict |
| `src/book.ts` | The Book of the CDU, a git store committed after every turn; seed in `book/` |
| `src/pi.ts` | The Pi session: the Meter as its only provider, `postgres-mcp`, the soul appended |
| `src/meter.ts` | Holds the key; daily tabs, persisted, readable by their holder |
| `soul/APPEND_SYSTEM.md` | The soul. The live copy is master's, so changes to it ride the PR |
| `sql/cdu-role.sql` | The read-write `cdu` role for the live database |

## HTTP

Bearer `CDU_TOKEN`. `GET /status`, `POST /talk {from: {id, name}, text, kind: ask|talk|answer}`, `GET /turns/{id}`, `GET /lines?after=N`, `GET /news?after=N`, `POST /tide/in`, `POST /tide/out`. Tide calls return 202; what came of them is news.

## Settings

| CDU | |
|---|---|
| `CDU_TOKEN` | What the World sends |
| `CDU_METER_TAB` | Its tab on the Meter, also Pi's key |
| `CDU_METER_URL` | Default `http://cdu-meter:8788/v1` |
| `GH_TOKEN` | Fine-grained: this repo, contents and pull requests, no workflows |
| `CDU_DATABASE_URL` | Optional: the live database as role `cdu`, for `postgres-mcp` |
| `PARTYTOWN_TEST_CONN` | The scratch Postgres the tests use |
| `CDU_MODEL`, `CDU_THINKING` | A model id from the Meter's list; `off`, `low`, `medium` or `high` |
| `CDU_ORIGIN`, `CDU_GITHUB_REPO`, `CDU_BASE`, `CDU_TIDE_BRANCH` | Defaults: this repo on GitHub, `master`, `cdu/tide` |

| Meter | |
|---|---|
| `METER_UPSTREAM_URL`, `METER_UPSTREAM_KEY` | Any OpenAI-compatible `/v1`, including Ollama at `http://host.docker.internal:11434/v1` |
| `METER_SECRET` | Admin port; the CDU never has it |
| `METER_TAB_TOKEN`, `METER_TAB_DAILY_USD` | The CDU's daily allowance |
| `METER_STATE` | Where tallies are kept across restarts |
| `METER_MODELS` | Default `/app/models.json`: allowed models with prices. `models.json` is OpenCode Go (`provider: opencode-go` makes Pi use its built-in provider through the Meter); `models.openrouter.json` is the OpenRouter set |

## Run

- Dev: set `CDU_UPSTREAM_URL` (and `CDU_UPSTREAM_KEY`, `CDU_GITHUB_TOKEN`) in the AppHost's user-secrets; the AppHost then builds and starts the CDU, the Meter and a scratch Postgres, and points the backend at it.
- Production: `kamal deploy -c config/deploy.cdu.yml` and, once, `kamal accessory boot all -c config/deploy.cdu.yml`. Secrets: `CDU_TOKEN`, `CDU_METER_TAB`, `CDU_METER_SECRET`, `CDU_OPENCODE_GO_KEY`, `CDU_GITHUB_TOKEN`, `CDU_DATABASE_URL`. `config/deploy.yml` gives the backend `Cdu__Url` and `Cdu__Token` (from `CDU_TOKEN`).
- The image bakes NuGet and pnpm caches. Build with `--pull`: a stale local SDK tag is an older preview than CI's.

## Tests

`npm test` (no Docker, no network: real git in temp dirs, a fake GitHub, a fake mind) and `npm run check`.
