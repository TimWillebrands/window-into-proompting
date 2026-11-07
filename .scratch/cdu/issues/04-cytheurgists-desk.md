# Phase 2: the Cytheurgists' desk and the tide

Status: resolved

Spec: `../spec.md` (the World side).

## What to build

- `Company.Kind` `cdu`, with legacy `works` read as `cdu`; the "works on the world" option in Work & Wants becomes the CDU kind.
- `CduApp` and `CduScreen` (rework of `WorksApp`/`WorksScreen`): one move per on-shift Life turn, `talk`, `answer` (only when it asked), `look`; the frame for Cytheurgists; world health; allowance.
- `CduGrain` (shrinks `CommissionsGrain`): exchanges and cursors, pure state.
- `Cytheurgy` (rework of `Worksite`) and `CduWatch` (rework of `WorksWatch`): replies reach the speaker as work memory; shift start and end, read from staff Occupations, call `tide/in` and `tide/out`.
- `GET /worlds/{party}/cdu` and `.../cdu/lines`; regenerate the API client.
- The `C:\CDU\` window, its story and mocks.

## Acceptance criteria

- [ ] A Cytheurgist on shift gets `talk` and `look`, and `answer` only after a question.
- [ ] Denise asks during her shift, and `tide/out` is called when it ends (fake CDU in a test cluster).
- [ ] Off shift: one plain line, no frame.
- [ ] No one but staff is told anything.

## Blocked by

- `03-cdu-container.md` (the HTTP contract)

## Comments

Done on `feat/cdu`. `CduApp`, `CduScreen`, `CduGrain` with pure `CduRules`, `Cytheurgy`, `CduWatch`, `CduController`; `works` Companies read as `cdu`. `CduWorldTest` (fake CDU in a test cluster) covers talk, a reply reaching only the speaker, answering an open question, look, shift start and end calling the tide, a dark CDU getting the edge again, and the secret. The tide is driven from the watch, reading staff Occupations: Life and shift steps are untouched. `C:\CDU\` window, story `Apps/CduApp` (checked rendering in the Storybook dev server; `storybook build` segfaults on Node 26, on master too) and mocks. Full backend suite: 1112 passed on a fresh AGE database.
