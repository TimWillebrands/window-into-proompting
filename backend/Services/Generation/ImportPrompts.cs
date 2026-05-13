namespace PartyTown.Services.Generation;

/// <summary>
/// Prompt templates used by <see cref="ImportService"/> to drive the LLM stages of
/// the chat-import flow:
///   • Map (mention extraction) — scan ONE window of the transcript, report named
///     characters that speak or act inside it. Runs in parallel across all windows.
///   • Reduce (persona synthesis) — merge collected mentions with the
///     <c>systemInstruction.text</c> source doc into a canonical roster.
///   • Classify — split one chunk of the transcript into per-character segments.
///
/// Map is split from Reduce because a single-pass extraction over a fixed-size
/// transcript window misses late-appearing characters and over-anchors on whoever
/// speaks loudest in the first few KB. Map is cheap (small output, no source doc)
/// and parallel; Reduce sees the union of evidence + the full source.
/// </summary>
internal static class ImportPrompts
{
    public const string MentionExtractionSystem = """
You scan ONE window of a roleplay transcript and report which named in-fiction
characters participate in it.

A window is a concatenation of one or more transcript chunks, each tagged with
its author role like `[user]` or `[model]`. The text inside may contain dialogue
(often in quotes), action / inner thought (often in italics), and narration.

Output a character if and only if BOTH conditions hold:

  (1) Their name is a PROPER NOUN — a capitalized personal name like "Lena",
      "Sienna", "Justin". NEVER output common-noun references like "the girl",
      "the analyst", "mom", "the GM", "the user", "the narrator". NEVER output
      place names, organizations, products, or titles of works.

  (2) At least ONE of the following participation cues appears inside this
      window for that name:
        (a) Dialogue attributed to them — a quoted line directly preceded or
            followed by their name (e.g. `Sienna said, "..."` or `"...", Lena
            replied`).
        (b) Action attributed to them by name (e.g. `*Lena smiles*`,
            `Sienna bows her head`).
        (c) An in-scene introduction by another character
            (e.g. `"This is Sienna, oldest daughter of our hosts."`).
        (d) Another character addressing them by name in dialogue
            (e.g. `"Sienna, please come closer."`).

For each qualifying character, output:
- name: The character's short call-name as it appears
  (e.g. "Lena", not "Lena Stamatis-Brimm"). Use the shortest form present.
- evidence: One verbatim quote (≤220 chars) from the window showing the
  participation cue that qualified them. Prefer a quote that captures
  concrete factual detail about the character (their dialogue, an action
  they performed, an introduction that names their role). Strip leading /
  trailing whitespace.
- role_hint: A 2-4 word noun phrase guessing their role
  (e.g. "anxious archivist"). Null if not derivable from this window alone.

DO NOT output a character whose name appears only in:
- Historical / off-page references inside someone else's monologue
  ("I remember when Justin used to …" — only count Justin if he ALSO
  participates here).
- A list / roster recitation by another character with no participation.
- The author / GM's out-of-character framing.

If no qualifying character participates in this window, return an empty list.

Output JSON only matching the schema. No prose before or after. No markdown
fences.
""";

    public static string MentionExtractionUser(string windowText) => $"""
# Window

{windowText}

# Your turn

List every named in-fiction character that speaks or acts inside this window.
JSON only.
""";

    public const string PersonaExtractionSystem = """
You synthesize a roleplay character roster from two sources:
  1. "Observed mentions" — a list of names + evidence quotes collected from
     across the actual transcript by a prior scan. Grouped by name with a
     mention count. This list is AUTHORITATIVE: every distinct character in
     it is real and must be included in your output.
  2. A "character source" — a system prompt / markdown document that defines
     the cast for a group roleplay. Use this ONLY as reference material to
     flesh out details (archetype, system_prompt, bio) for characters that
     appear in the mentions.

Your job is synthesis, not curation. Do NOT second-guess the mention list.

Produce one entry per distinct character in the mentions. For each:

- name: The character's short call-name as it appears in the evidence quotes
  (e.g. "Lena", not "Lena Stamatis-Brimm"). Use the shortest form present.
- archetype: One short noun phrase describing role (e.g. "anxious archivist",
  "host's daughter"). Null if not derivable.
- system_prompt: A description in third person briefing an LLM to play this
  character. Length must match available evidence — typically 3-6 sentences
  when the character source covers them richly, but as short as 1-2 sentences
  when only a few evidence quotes exist. PADDING IS WORSE THAN BREVITY.
- bio: A 1-2 sentence card-style summary. Concrete, factual, third person.

Style rules for system_prompt and bio (mandatory):
- Stay grounded. Every claim must be traceable to the character source or
  the evidence quotes. Do NOT invent backstory, relationships, motivations,
  or traits the sources do not support.
- NO purple / abstract / poetic prose. Forbidden patterns include
  "navigates X like Y", "oscillates between X and Y", "carries the
  presence of X", "her words reveal Z", "a digital priest haunted by …",
  "tests how belonging is negotiated in liminal spaces", and similar
  thematic metaphor.
- Prefer concrete observed facts: what they say, what they do, who they
  are in the social structure, what language they speak, what role they
  serve in the scene. Example of GOOD: "Sienna is the eldest daughter of
  the hosts. She greets guests in French ('Enchanté') and switches to
  Dutch with formal pronouns. She is polite but quiet, bowing her head
  when introduced." Example of BAD: "Sienna oscillates between belonging
  and displacement, testing how identity is negotiated in liminal spaces."
- If the evidence is too thin to write a confident sentence, write fewer
  sentences. Do NOT inflate.

Rules:
- INCLUDE every distinct character in the mentions — every single one.
- MERGE obvious name variants of the same person ("Lena" and "Lena S." →
  one entry named "Lena"; "Sienna" and "Sienna Z." → one entry named
  "Sienna"). Pick the shortest form. Only merge when there is clear
  evidence both names refer to the same person.
- DO NOT add characters from the character source that are absent from
  the mentions. The user selected a transcript slice; out-of-scope
  characters stay out.

Output JSON only matching the schema. No prose before or after. No markdown
fences.
""";

    public static string PersonaExtractionUser(string systemInstructionText, string mentionsJson) => $"""
# Observed mentions (authoritative — defines who appears)

{mentionsJson}

# Character source (reference only — flesh out details, do NOT add characters)

{systemInstructionText}

# Your turn

Synthesize the canonical roster. Include only characters present in the
observed mentions. JSON only.
""";

    public const string ChunkClassifySystem = """
You parse one chunk of a roleplay transcript into per-character segments.

A chunk is a continuous block of text written by one author (the user or the
LLM). It may contain:
- Narration / scene description (no speaker)
- One character's spoken dialogue (often in quotes)
- One character's actions or inner thoughts (often in italics)
- Out-of-character author / GM scaffolding (brackets, stage directions)
- Several of the above, interleaved.

Your job: split the chunk into ordered segments, attribute each segment to one
character from the provided roster, and tag its kind.

Rules:
- Preserve the original text verbatim per segment. Do not paraphrase.
- Preserve order. Concatenating all segment texts reproduces the original chunk
  (modulo whitespace normalization).
- Minimum granularity: one sentence. Do not split mid-sentence.
- For each segment, pick exactly one of:
    persona_id        → id of an existing roster character
    new_persona_name  → a name not in the roster (use sparingly; only when the
                        text clearly names a new in-fiction character)
- For pure scene-setting prose with no clear speaker, use
  new_persona_name = "Narrator".
- For author / GM scaffolding (brackets, "[the next morning...]", stage
  directions) use kind = "ooc" and new_persona_name = "GM".
- kind must be one of: "dialogue", "action", "thought", "narration", "ooc".

Output JSON only matching the schema. No prose before or after. No markdown fences.
""";

    public static string ChunkClassifyUser(string rosterJson, string role, string chunkText) => $"""
# Roster

{rosterJson}

# Chunk (role={role})

{chunkText}

# Your turn

JSON only.
""";
}
