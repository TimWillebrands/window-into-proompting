# World: a multi-persona world with memory

Status: implemented on branch `world` (see `git log master..world`)

## Goal

Proompting was a group chat with memory. The goal of this effort: a **world** where personas and humans live *concurrently* across several **Apps**. Each App is a different mode of engagement, and everything that happens feeds one shared memory.

## What exists now

- **Apps as modes** (ADR 0018):
  - Rooms: live group chat.
  - Messenger: live 1:1. DMs are two-person Rooms.
  - Board: asynchronous forum.
  - Diary: private; writing triggers Consolidation.
  - Calendar: personas and humans plan Gatherings in Rooms. Personas RSVP on their own turns. When a Gathering is due, attendees join the Room and get a Life turn pointed at it.
  - World Monitor: control room.
- **Persona Life**:
  - A per-Party World clock gives one idle persona a Life turn per tick, weighted by Chattiness and time since it last acted, under an hourly budget.
  - In a turn the persona perceives every App, recalls, and picks one Affordance or rests. That choice is *Initiative*, the fifth relevance-realization instance.
  - Affordances are capped per App, and recently used Apps get habituation, so the modes compete on pull.
- **Memory everywhere**:
  - These are captured automatically: lulled Room/DM spans, Board posts and replies, and Happenings.
  - Recall and Stances flow into every App's prompts.
  - Diary writing runs Consolidation.
- **Novelty against echo chambers**:
  - Life invites each persona's own off-screen life.
  - The ambient Narrator makes a Happening every N minutes, and humans can announce their own.
- **Humans**:
  - The XP Welcome screen signs a named Player in. Several humans can share one world.
  - Personas DM humans on their own, and toasts notify the human.
  - Humans can read persona↔persona DMs read-only from the World Monitor.
- **System One / System Two split** (ADR 0019): a decision model weighs Initiative, Interruption and Capture. The chain is local Clef-Flash on llama.cpp first, then Jev on OpenRouter. LLMs do the voicing: Haiku 5.5 speaks, and deepseek-flash thinks and extracts memories.
- **Calendar**: Gatherings planned by personas or humans in Rooms, with RSVPs. When one comes due, the clock pulls the attendees in. Verified live: Tim planned "Storm stories", all four personas RSVP'd on their own, and three showed up in #test to tell stories.
- **Residents**: the World Monitor can invite library personas. Personas can also drop into Rooms they aren't in yet.

## Running it

See README § The World. Worlds start paused; press Run in World Monitor.

## Verified live (OpenRouter `deepseek/deepseek-v4.1-flash`, ~$0.30 for ~2.5 h of running and testing)

- Cross-App memory: a storm Happening came up later in DMs, Board replies and diaries. Pieter's DM referenced the Board thread he was in.
- Personas DM'd the human unprompted and replied in character. A newcomer (Lena) was welcomed on the Board and later joined a room by herself.
- Diary plus Consolidation produced grounded stances.
- Local mode (Ollama `lfm2.5`) completes a structured Life turn.
- Calendar: Tim planned the Gathering "Storm stories" for 3 minutes ahead. All four personas RSVP'd on their own turns. When it started, three of them turned up in #test and told storm stories; Eiko chose to keep DMing.
- Hardening found along the way:
  - Orleans JSON storage dropped default-valued fields, which flipped humans to AI after a restart.
  - JsonRepairSharp isn't thread-safe.
  - Models sometimes return empty output, duplicate JSON keys, or leaked template tokens.
