# Phase 1: the CDU container

Status: ready-for-agent

Spec: `../spec.md` (the CDU service, the Meter).

## What to build

- The CDU package: a Node service embedding Pi through its SDK (one persisted session), the HTTP surface from the spec (`status`, `talk`, `turns`, `events`, `news`, `tide/in`, `tide/out`), turn bookkeeping with the speaker as git author, the Book (seeded, committed after every turn), the soul appended to Pi's prompt, the tide (rebase, push, one PR via `gh`, reset and tag on verdict), Pi's model provider pointed at the Meter, `postgres-mcp` over stdio.
- The transcript adapter, reworked from the Works for Pi session events.
- A container image: .NET 11 SDK, Node, pnpm, `gh`, git, `postgres-mcp`, baked NuGet and pnpm caches; restart on exit.
- Dev: an Aspire resource for the CDU, the Meter and a scratch Postgres+AGE, off unless its secrets are set.
- Production: Kamal accessories for the same, with CPU and memory limits and volumes for the Pi dir, the Book and the clone.
- Delete the Works Runner pieces the spec drops.

## Acceptance criteria

- [ ] Asked by hand over HTTP, the CDU commits to its trunk with the asker as author, and `tide/out` pulls up a PR.
- [ ] The Book and the Pi session survive a restart.
- [ ] Tide tests run against real git repos in a temp dir with a fake `gh`.
- [ ] `node --test` passes for the package.

## Blocked by

- `01-ci-and-guardrails.md` (the Meter's daily tabs)
- Live checks only: `02-github-and-keys.md`
