---
name: cog-sci-engineer
description: Use when adding, changing or debugging how personas behave, such as when they speak, notice, act, remember, feel or want; or when touching Reflex, Life/Initiative, the response pipeline, Recall/Capture, the lived self, Desires or System One questions.
---

# Cognitive Science Engineering

Personas aren't scripted. Their behaviour comes from modelled cognitive processes, so change the process, not the outcome.

## Frame

Vocabulary, not knobs: name what you're building in these terms before touching a mechanism. Full definitions are in `CONTEXT.md`.

**Relevance Realization (RR)**: the one faculty behind every "what matters now?" judgement, realized by balancing opposing pulls, never by a fixed rule. It runs as six instances:

| Instance | Timescale | Question |
| --- | --- | --- |
| Interruption | ms | Is this worth dropping my draft for? |
| Urge | s | Is this beat worth speaking into? |
| Reflex | s, stimulus-driven | Did I notice it, what do I do, and when? |
| Initiative | min, stimulus-free | Nothing asks anything of me: what's worth doing? |
| Recall | days, backward | Which past moments belong in this beat? |
| Capture | days, forward | What's worth remembering at all? |

- **Weigh ⇄ voice**: System One weighs (probabilities, sampled, no prose); the LLM voices and may decline. Ask which one a change is first (ADR 0019).
- **Opponent processing**: keep tensions as tensions (recent ⇄ relevant, speak ⇄ hold back, finish ⇄ interrupt, explore ⇄ exploit). Never fold them into one formula or an argmax.
- **Parasitic processing**: a loop that feeds itself (the story retold until it fills recall, a motif spreading through the cast). The cure is restoring the counter-pull, not a ban.
- **A damper is an expectation**: what a persona has already seen, said or done is predicted, so it pulls less; something new resets it (Andersen, Miller & Vervaeke 2022). Bans push voices together (ADR 0021 #32).
- **The code shapes the landscape, the persona chooses**: RR can't be formalized, only emulated (Jaeger et al. 2024). Code decides what's on offer and foregrounded, never what's relevant.
- **Salience ⇄ Urge**: Salience is pull from the Arena, always relational; Urge is push from within. Memories under salience form the salience landscape: use strengthens, disuse fades.
- **Agent–arena**: a Persona's view lives on edges to shared entities, never on the entities. Affordances are relational too: what an App offers *this* Persona *now*.
- **Consolidation in rest**: Capture writes facts and recollections; beliefs (Stances) form later.
- **Having ⇄ being**: having-desires want a thing; being-desires want to become someone.

**Four kinds of knowing (4P)** map onto memory layers:

| Knowing | Layer |
| --- | --- |
| Propositional (that) | Event, Concept |
| Perspectival (what it was like from here) | Recollection |
| Participatory (by being in relation) | Stance, Role, Desire |
| Procedural (how) | Chattiness, Impulsivity, Occupation |

## Mechanisms

Paths are from the repo root. Find symbols by name, since line numbers drift.

| Instance | Code | Rule today |
| --- | --- | --- |
| Interruption | `backend/Services/ResponsePipeline/RaceTrigger.cs`, `PersonaSalienceService` | System One noul vs `CancelThreshold`, before `PnrTokens` |
| Urge | `backend/Services/ResponsePipeline/UrgeMath.cs`, `PersonaDecisionService` | cheap math pre-filter, then a "Room:" nudge for the Decision phase |
| Reflex | `backend/World/Reflex/ReflexMath.cs`, `ReflexWeighing`, `PersonaReflexGrain` | notice filter, System One choice × trait bias, sampled; torn → System Two |
| Initiative | `backend/World/PersonaLifeGrain.cs` (`Pull`, `ChoiceTemperature`), `backend/World/Apps/*`, `backend/World/Self/Hooks.cs` | integer pulls prune the menu, System One samples, voicing may decline |
| Recall | `backend/World/Self/PersonaRecall.cs` (every beat), `backend/Services/Memory/SalienceMath.cs`, `RecallRanking.cs` | weight × decay + use bonus × refractory, two arms, 3-day horizon, then the recall dampers |
| Capture | `backend/Services/Memory/MemoryExtractor.cs`, `backend/World/LullCaptureGrain.cs` | System One gate, then LLM extraction |
| Who acts next | `backend/World/ClockPolicy.cs`, `WorldClockGrain.cs` | weighted by Chattiness, idle time, notifications, availability |

- **System One**: `backend/Services/SystemOne/SystemOne.cs` (`ISystemOneJudge`, `SampleChoice`). Always there (ADR 0044): never branch on "no judge". A missing answer means "no view", so keep the caller's fallback.
- **Dampers**:
  - recall: `SalienceMath.Refractory`, `backend/World/Self/StaleRecall.cs`, `RecallVariety.cs`
  - options: Life's recent-app count, `Settling`, `Shifts.Damping`, `BoardApp.ThreadFatigue`
  - attention: `ReflexMath.Habit` / `Surprise`
  - speech: `backend/World/Self/VoiceWear.cs`, `UtteranceRhythm.cs`, `SpeechCleanup.cs`
  - room: `ConversationThermostat.cs`
- **State**:
  - `SelfMath` in `backend/World/Self/PersonaSelfGrain.cs` (battery, valence, availability, threads)
  - `Ties` in `PersonaTiesGrain.cs`
  - `MomentAppraisal.cs`, `FollowThrough.cs`, `ChatAttention.cs`
  - rendered by `backend/Services/ResponsePipeline/LivedSelf.cs`
- **Slow learning**:
  - `backend/Services/Memory/ConsolidationService.cs`, `StanceProjection.cs`
  - `backend/World/Desires/`
  - `backend/World/Relations/`
- **Bench**: `tools/bench/Probes/`:
  - `DecisionProbes`, `ReflexProbes`, `RecallScenarioProbes`, `StanceProbes`
  - `BoardProbes`, `PlacesProbes`, `WorkProbes`

## Building

1. **One question, one place.** Find every path that answers the question you're changing (Room pipeline, Reflex, Life, Board, Places), and change it where they converge. Recall converges in `PersonaRecall`. A Room message runs weigh → voice: the Urge pre-filter (cost), Reflex (System One), then the Decision phase (System Two, may decline).
2. **Tune before you add.** If a process exists but misbehaves, fix the general process; don't add a special path beside it.
3. **Layer on an existing process.** Most behaviour is an input to one. For example, entering a room creates a Percept that Reflex weighs; it isn't a new trigger.
4. **Facts go in the state, not in pulls.** If System One can read it from the state, a bias on top counts it twice (ADR 0031). Biases are only for what the state can't carry: numeric traits and norms.
5. **Give every self-reinforcing pull its counter-pull,** written as expectedness, not as a ban.
6. **Show the behaviour it buys.** Change a lever only with a probe that shows the difference.
7. **A real gap needs a proposal.** Ground it in Vervaeke (or other cog-sci) and propose it to the user before building.
