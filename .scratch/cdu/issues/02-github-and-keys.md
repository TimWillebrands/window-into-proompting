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
