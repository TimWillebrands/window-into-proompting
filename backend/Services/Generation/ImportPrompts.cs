namespace PartyTown.Services.Generation;

/// <summary>
/// Prompt templates used by <see cref="ImportService"/> to drive the two LLM stages of
/// the chat-import flow:
///   • Task A — extract roleplay character definitions from a Gemini-AI-Studio
///     <c>systemInstruction.text</c> block into persona stubs.
///   • Task B — split one chunk of a roleplay transcript into per-character segments.
///
/// The two stages run as separate prompts (not stacked) to avoid task-switching cost
/// and to keep the ~10 KB systemInstruction off the hot path of the per-chunk
/// classifier, which runs hundreds of times per import.
/// </summary>
internal static class ImportPrompts
{
    public const string PersonaExtractionSystem = """
You extract roleplay character definitions from author-supplied prompts.

You will be given a markdown document that defines several characters for a
group roleplay. Identify each distinct character. For each, produce:

- name: The character's short call-name as it appears in dialogue
  (e.g. "Lena", not "Lena Stamatis-Brimm"). If only one form appears, use that.
- archetype: One short noun phrase describing role
  (e.g. "anxious archivist"). Null if not derivable.
- system_prompt: A 3-6 sentence description in third person describing the
  character as if briefing an LLM to play them. Stay grounded in the source —
  do NOT invent facts.
- bio: A 1-2 sentence card-style summary. Vivid, brief, third person.

Do not include the narrator, the user, the GM, or out-of-character framing.
Only named in-fiction characters.

Output JSON only matching the schema. No prose before or after. No markdown fences.
""";

    public static string PersonaExtractionUser(string systemInstructionText) => $"""
# Character source

{systemInstructionText}

# Your turn

Extract every distinct in-fiction character. JSON only.
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
