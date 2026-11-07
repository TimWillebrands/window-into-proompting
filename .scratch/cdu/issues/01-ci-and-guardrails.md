# Phase 0a: CI, the CDU's database role and a daily allowance

Status: ready-for-agent

Spec: `../spec.md` (Phase 0). The agent's half of Phase 0; Tim's half is `02-github-and-keys.md`.

## What to build

- A GitHub Actions workflow that runs on every PR to `master` and on pushes to it:
  - backend: build, then the backend tests against an `apache/age` service on `localhost:5455` with the test fixture's credentials, init scripts applied;
  - frontend: install, `pnpm check`, `pnpm test`, `pnpm build`;
  - the CDU package: `node --test`.
- A SQL script that creates a read-write `cdu` login role for the live database (password from a psql variable), with rights on Orleans storage and the AGE memory graph.
- The Meter learns daily tabs: a USD allowance that resets at UTC midnight, declared in config, persisted to a file, readable by its holder with its own token.

## Acceptance criteria

- [ ] The workflow is valid YAML, uses only first-party actions, and needs no secrets.
- [ ] A fresh Postgres+AGE service gets the repo's init scripts before tests run.
- [ ] The role script is idempotent and grants what the CDU needs to read and write grain state and the memory graph.
- [ ] Meter tests cover: daily reset, persistence across a restart, self-read, and that a tab holder can't raise its own cap.

## Blocked by

None.
