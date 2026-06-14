using PartyTown.Model;

namespace PartyTown.Bench.Probes;

/// <summary>
/// Scenario material for the decision probes — a small fixed cast and history builders, in
/// normal C# close to the probes (ADR 0011). GUIDs are stable so artifacts diff cleanly across
/// runs. As the probe library grows this becomes the seed corpus for a future eval harness.
/// </summary>
public static class BenchCast
{
    public static readonly Guid TimId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid VladId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DeniseId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid RoomId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>The human in the room — view used in the participants list.</summary>
    public static ParticipantView Tim => new(TimId, "Tim", DriverKind.User);

    /// <summary>Brooding, low chattiness — pings rarely.</summary>
    public static SelfView Vlad => new(
        Id: VladId,
        Name: "Vlad",
        Driver: DriverKind.LLM,
        Bio: "A centuries-old vampire, world-weary and dry. Speaks little, lands hard when he does.",
        SystemPrompt: null,
        Chattiness: 0.2,
        Impulsivity: 0.3);

    /// <summary>Bubbly, high chattiness — chimes in often.</summary>
    public static SelfView Denise => new(
        Id: DeniseId,
        Name: "Denise",
        Driver: DriverKind.LLM,
        Bio: "An over-caffeinated event planner. Warm, fast-talking, never met a silence she liked.",
        SystemPrompt: null,
        Chattiness: 0.8,
        Impulsivity: 0.7);

    public static IReadOnlyList<ParticipantView> All => new[]
    {
        Tim,
        new ParticipantView(VladId, "Vlad", DriverKind.LLM),
        new ParticipantView(DeniseId, "Denise", DriverKind.LLM),
    };

    /// <summary>One user message into a quiet room. The content is a statement (no '?', no
    /// persona names) so it does NOT trip the urge≥0.9 auto-respond shortcut — that would skip
    /// the LLM and defeat the cold-open probe (see the trap in ADR 0011 / handoff).</summary>
    public static IReadOnlyList<ChatMessage> ColdOpenHistory(string content) => new[]
    {
        new ChatMessage
        {
            MessageId = 1,
            Content = content,
            SenderType = "user",
            SenderId = TimId,
            ChatGroupId = RoomId,
        },
    };
}
