# System One is required

> Status: accepted. Amends ADR 0019, 0021 (#8), 0026, 0031 and 0034; builds on ADR 0040.

Since ADR 0040, System One always answers: a Decisions provider when one is up, LLM emulation when none is. Even so,
every beat-path caller read `ISystemOneJudge.Enabled` and kept a second behaviour for when it was false:
- Initiative ran as one combined LLM call.
- Capture ran ungated.
- There was no thermostat, and no mood or warmth appraisal.
- Reflex didn't run.
- Interruption used its own LLM salience call.

Only an environment with no Decisions provider and `SYSTEM_ONE_EMULATE` off ever ran that mode. So every behaviour
change had to be reasoned about twice, the second time for a mode nobody ran.

## Decision

- **A System One judge and an LLM are basic expectations.** `Enabled` and `SYSTEM_ONE_EMULATE` are gone.
  `ProviderJudge` routes to a Decisions provider, and when none answers it emulates with a General-tier LLM.
- **Every caller keeps only its judged path.**
  - Initiative always weighs with System One, then voices the winner (ADR 0019).
  - Interruption always asks System One, and its separate LLM salience call is gone.
- **A missing answer means "no view", not "no".** Callers keep their own fallback:
  - a Practice fit stays unknown rather than 0;
  - a catalogue pick falls back to the best word match.
- **Reflex still steps aside** for paused Worlds and scenario Rooms; the Urge path covers those.

## Consequences

- One behaviour per lever: about 15 forks deleted.
- An environment without any Decisions provider now emulates on every beat. That costs more, but it's never silent.
- Tests run System One through a scripted judge (`backend-test/Infrastructure/MenuJudge.cs`) instead of switching it
  off.
