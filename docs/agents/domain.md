# Domain Docs

Before exploring: read `CONTEXT.md` (glossary, architecture) and the `docs/adr/` ADRs touching your area.

- Use `CONTEXT.md` vocabulary in issue titles, refactors, hypotheses and test names. Terms it lists under _Avoid_ are out. A missing term = a real gap (flag it) or invented language (reconsider).
- ADR numbers are stable and cited from code. Gaps = folded into a successor or deleted as superseded; `git log --diff-filter=D -- docs/adr/NNNN*` finds them.
- If your output contradicts an ADR, say so: _Contradicts ADR 0029 (public read) — but worth reopening because…_
