# Vervaeke × Proompting Resources

## Knowledge

### Primary (Vervaeke)
- [Paper: *Relevance Realization and the Emerging Framework in Cognitive Science* — Vervaeke, Lillicrap & Richards, J. Logic & Computation 22(1), 2012](http://contrastiveconvergence.net/~timothylillicrap/files/articles/relevance%20realization%20as%20an%20emerging%20framework%20in%20cogsci.pdf)
  The canonical academic statement of RR: frame problem, combinatorial explosion, opponent processing, why relevance cannot be a computed property. Use for: any "why can't we just score it with one formula" question. ([publisher page](https://academic.oup.com/logcom/article-abstract/22/1/79/1007787))
- [Lecture series: *Awakening from the Meaning Crisis* — johnvervaeke.com episode index](https://johnvervaeke.com/series/awakening-from-the-meaning-crisis/)
  50 episodes. The cognitive-science core for this mission: Ep. 27–32 (frame problem, RR, salience landscaping, optimal grip); 4P kinds of knowing recur throughout. Use for: intuition-building; the paper for precision.

### In-repo (the applications)
- `CONTEXT.md` — the glossary where Vervaeke terms are canonised as domain language (§ Relevance realization, § Memory, § Arena). The single most load-bearing document.
- `docs/adr/0015-recall-as-relevance-realization.md` — recall as RR: salience formula, two-arm quota union as opponent processing, beat-path LLM budget.
- `docs/adr/0016-stance-floor-and-auto-appending-consolidation.md` — participatory layer; consolidation-in-rest.
- `docs/adr/0010-two-phase-persona-response.md` + `0001-stop-signal-race-emote-on-cancel.md` — Urge and Interruption instances.
- `~/.claude/skills/proompter/knowledge/cognitive-science.md` — Tim's own distillation of RR applied to prompting (salience ≠ importance ≠ relevance, agent–arena affordances, opponent dials). High-trust: it's the house interpretation.
- Code anchors: `backend/Services/Memory/SalienceMath.cs`, `backend/Services/ResponsePipeline/UrgeMath.cs`, `RaceTrigger.cs`, `RecallDeployment.cs`, `MemoryExtractor.cs`, `ConsolidationService.cs`.

## Wisdom (Communities)

- [Awakening from the Meaning Crisis Discord](https://discord.com/invite/WBvRK3EtRu)
  ~3.5k members; Vervaeke has run Q&As there. Use for: testing your reading of RR/4P against people who study it closely.
- [Vervaeke Foundation Patreon community](https://www.patreon.com/posts/discord-server-36594221)
  Paid tier; closer to Vervaeke's own circle. Optional.

## Gaps

- No known community at the intersection (Vervaeke × AI-agent architecture). Nearest substitutes: the Discord above for theory-checking; AI-agent forums for the engineering half. Worth a future search for "relevance realization AGI/agents" researchers (e.g. work following the 2012 paper into AI).
