# Phase 3: river feedback

Status: ready-for-agent

Spec: `../spec.md` (river feedback, the tide).

## What to build

- The CDU notices verdicts and check results on its PR (`gh pr view`), resets the trunk on a verdict, tags closed work, and writes the fact to its journal.
- Those facts reach the World as news: merged and closed reach all staff with a mood; failing checks reach staff as work memory.
- The `C:\CDU\` window shows the PR's state and checks.

## Acceptance criteria

- [ ] A merge or a rejection reaches Denise and Justin as news, once.
- [ ] After a rejection the trunk equals `master` and a `drowned/...` tag holds the work.
- [ ] Failing CI reaches staff once per head commit.

## Blocked by

- `04-cytheurgists-desk.md`
