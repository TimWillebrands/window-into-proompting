using PartyTown.Model;

namespace PartyTown.Bench.Probes;

public static class BenchCast
{
    public static readonly Guid SamId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid VladId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DeniseId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid RoomId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public static ParticipantView Sam => new(SamId, "Sam", DriverKind.User);

    public static SelfView Vlad => new(
        Id: VladId,
        Name: "Vlad",
        Driver: DriverKind.LLM,
        Bio: "A centuries-old vampire, world-weary and dry. Speaks little, lands hard when he does.",
        SystemPrompt: null,
        Chattiness: 0.2,
        Impulsivity: 0.3);

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
        Sam,
        new ParticipantView(VladId, "Vlad", DriverKind.LLM),
        new ParticipantView(DeniseId, "Denise", DriverKind.LLM),
    };

    public static IReadOnlyList<ChatMessage> ColdOpenHistory(string content) => new[]
    {
        new ChatMessage
        {
            MessageId = 1,
            Content = content,
            SenderType = "user",
            SenderId = SamId,
            ChatGroupId = RoomId,
        },
    };

    public static IReadOnlyList<ChatMessage> PersonaSays(Guid senderId, string content) => new[]
    {
        new ChatMessage
        {
            MessageId = 1,
            Content = content,
            SenderType = "assistant",
            SenderId = senderId,
            ChatGroupId = RoomId,
        },
    };
}
