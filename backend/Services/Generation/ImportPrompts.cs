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
characters appear inside it.

A window is a concatenation of one or more transcript chunks, each tagged with
its author role like `[user]` or `[model]`. The text inside may contain dialogue
(often in quotes), action / inner thought (often in italics), and narration.

For each distinct named character that speaks, acts, or is directly addressed
inside this window, output:
- name: The character's short call-name as it appears in the prose
  (e.g. "Lena", not "Lena Stamatis-Brimm"). If only one form appears, use that.
- evidence: One short verbatim quote (≤120 chars) from the window where this
  character speaks or acts. Strip leading/trailing whitespace.
- role_hint: A 2-4 word noun phrase guessing their role
  (e.g. "anxious archivist"). Null if not derivable from this window alone.

Do NOT include:
- The narrator (pure scene description with no speaker).
- The user (the human author) or the GM / out-of-character framing
  (brackets, "[the next morning...]", stage directions).
- Pronouns ("she", "him") — only proper names.
- Names that appear only as mentions in someone else's dialogue without
  themselves acting inside this window.

If no in-fiction named character speaks or acts in this window, return an
empty list.

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
  1. A "character source" — a system prompt / markdown document that defines
     the cast for a group roleplay. May be sparse, may name characters that
     never actually appear, may omit characters that do appear.
  2. "Observed mentions" — a list of names + evidence quotes collected from
     across the actual transcript by a prior scan. Grouped by name with a
     mention count and up to 3 short quotes per name.

Produce one entry per distinct in-fiction character. For each, produce:

- name: The character's short call-name as it appears in dialogue
  (e.g. "Lena", not "Lena Stamatis-Brimm"). Use the shortest form that appears
  in the transcript itself.
- archetype: One short noun phrase describing role
  (e.g. "anxious archivist"). Null if not derivable.
- system_prompt: A 3-6 sentence description in third person describing the
  character as if briefing an LLM to play them. Stay grounded in the source
  and evidence — do NOT invent facts.
- bio: A 1-2 sentence card-style summary. Vivid, brief, third person.

Merging rules:
- Treat name variants of the same person as one character ("Lena" and "Lena S."
  → "Lena"). Pick the shortest form that occurs in the evidence quotes.
- Include a character if EITHER the character source defines them OR they
  appear in the mentions with count ≥ 2. Drop one-off names that are likely
  typos or mis-extractions.
- Do not include the narrator, the user, the GM, or out-of-character framing.
  Only named in-fiction characters.

Output JSON only matching the schema. No prose before or after. No markdown
fences.
""";

    public static string PersonaExtractionUser(string systemInstructionText, string mentionsJson) => $"""
# Character source

{systemInstructionText}

# Observed mentions

{mentionsJson}

# Your turn

Synthesize the canonical roster. JSON only.
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
