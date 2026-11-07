# Phase 0b: GitHub settings, token and keys

Status: ready-for-human

Spec: `../spec.md` (Phase 0). Needs Tim: these are account settings and secrets.

## What to do

1. **Ruleset on `master`** in `TimWillebrands/window-into-proompting`: require a pull request, block force pushes and deletion. Optionally require the CI checks from `01`.
2. **Fine-grained token** for the CDU: this repo only; Contents read-write, Pull requests read-write, Metadata read. **No Workflows.** Store it as `CDU_GITHUB_TOKEN` (Kamal secret, and AppHost user-secret `cdu-github-token` in dev).
3. **The CDU's own OpenRouter key**, with a credit limit on OpenRouter's side as a second fence. Store it as `CDU_OPENROUTER_KEY` (`cdu-openrouter-key` in dev).
4. **The `cdu` database role**: run `tools/cdu/sql/cdu-role.sql` against production with `psql -v cdu_password=...`, and store the connection string as `CDU_DATABASE_URL`.
5. **Shared secrets** between World, CDU and Meter: `CDU_TOKEN` (World to CDU), `CDU_METER_TAB` (CDU to Meter), `CDU_METER_SECRET` (Meter admin). `openssl rand -hex 24` each.

## Acceptance criteria

- [ ] A test PR runs CI.
- [ ] `git push origin master` is refused.
- [ ] The token can push a branch and open a PR, and is refused on a change under `.github/workflows/`.

## Blocked by

- `01-ci-and-guardrails.md` (for the CI checks and the role script)

## Comments

2026-11-07, deployed (`master` a35db50, prod DB dumped first to `/data/backups/partytown-20261107-165548-pre-cdu.dump` on the server):

- Done: steps 4 and 5. The `cdu` role exists in prod and logs in from the CDU container. `CDU_TOKEN`, `CDU_METER_TAB`, `CDU_METER_SECRET`, `CDU_DB_PASSWORD` and `CDU_DATABASE_URL` are generated in `.kamal/secrets`. Backend, frontend and `proompting-cdu` (with `-meter` and `-db`) run in prod; the backend sees the CDU up.
- Left: steps 1 to 3. `CDU_OPENROUTER_KEY=` and `CDU_GITHUB_TOKEN=` are empty in `.kamal/secrets`, so the Meter gets a 401 upstream and the CDU can't push. Fill them in, then `kamal accessory reboot meter -c config/deploy.cdu.yml` and `kamal app boot -c config/deploy.cdu.yml`.
- Prod has no CDU Company yet, and its World is paused with two placeholder residents.

2026-11-07, later: the CDU thinks through OpenCode Go (`CDU_OPENCODE_GO_KEY`, behind the Meter, $1/day) and has its GitHub token (`CDU_GITHUB_TOKEN`). In the prod container `gh` is logged in, a push dry-run to `cdu/tide` is authorized, and PRs, check runs and statuses are readable. Still open: **no ruleset or branch protection on `master`**, so the token can push to it. `CDU_OPENROUTER_KEY` is unused.
