namespace PartyTown.Services.Memory;

/// <summary>
/// Perspective-fidelity policy for Recollection snippets — the write-side guard that keeps
/// corrupted memories out of the graph without throwing away the moment.
///
/// A snippet is read back later by ITS person alone, so "you" must mean that person and
/// everyone else must be named. Small extraction models reliably fumble this perspective
/// transformation ("You heard me announce I'm quitting" stored for the person who SPOKE)
/// while getting the neutral third-person Event description right — bench: every corrupted
/// draft in a run sat next to a perfectly good description. So instead of dropping a
/// corrupted draft (which erases the moment for that persona entirely), the capture path
/// substitutes a snippet templated from the Event description: the model does what it's
/// good at (neutral summary + weight judgment), code does the perspective. The weight is
/// the model's — the salience call wasn't the corrupted part.
/// </summary>
public static class RecollectionFidelity
{
    private const int MaxSnippetChars = 500;

    /// <summary>
    /// True when the snippet contains a first-person pronoun OUTSIDE quoted speech.
    /// "You announced \"I quit\" to Vlad" is fine (the pronoun is quoted, attributable);
    /// "You heard me announce I'm quitting" is not — outside quotes, "me"/"I'm" has no
    /// referent for the person reading the memory back later, and in practice marks a
    /// swapped subject.
    /// </summary>
    public static bool HasUnattributedFirstPerson(string snippet)
    {
        // Blank out straight- and curly-quoted spans, then scan what's left.
        var unquoted = System.Text.RegularExpressions.Regex.Replace(
            snippet, "\"[^\"]*\"|“[^”]*”|‘[^’]*’", " ");
        // Singular first person only — "we"/"us" can legitimately include the rememberer
        // ("Denise invited us all"). Bare "i" also catches curly-apostrophe contractions
        // ("I’m") since the apostrophe is a word boundary.
        return System.Text.RegularExpressions.Regex.IsMatch(
            unquoted,
            @"\b(i|i'm|i've|i'd|i'll|me|my|mine|myself)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Sanitizes one persona's draft against the Event description. A clean draft passes
    /// through; a corrupted one keeps its weight but trades its snippet for the templated
    /// description — deliberately third-person ("You remember: Denise announced …"):
    /// slightly stiff to read back, never wrong about who did what.
    /// </summary>
    public static (RecollectionDraft Draft, bool Substituted) Sanitize(
        RecollectionDraft draft, string eventDescription)
    {
        if (!HasUnattributedFirstPerson(draft.Snippet))
            return (draft, false);

        var fallback = $"You remember: {eventDescription.Trim()}";
        if (fallback.Length > MaxSnippetChars)
            fallback = fallback[..MaxSnippetChars];

        return (draft with { Snippet = fallback }, true);
    }
}
