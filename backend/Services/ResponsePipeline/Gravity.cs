using System.Text;
using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Self;

namespace PartyTown.Services.ResponsePipeline;

public static class Gravity
{
    public const string Key = "gravity";
    public static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(1200);
    public const int Lines = 6;
    public const int LineClip = 240;
    public const int AnsweringClip = 600;
    public const int IntentClip = 600;
    public const int BioClip = 400;

    public static NoulQuestion Question(string name)
        => new($"What {name} is about to say needs more than a line or two.");

    public static string? Bio(SelfView self, string? lived)
    {
        var bio = lived is not null && LivedSheets.Bio(lived) is { } livedBio ? livedBio
            : !string.IsNullOrWhiteSpace(self.Bio) ? self.Bio
            : !string.IsNullOrWhiteSpace(self.SystemPrompt) ? LivedSheets.Bio(self.SystemPrompt)
            : null;
        return string.IsNullOrWhiteSpace(bio) ? null : WorldStream.Clip(bio.Trim(), BioClip);
    }

    public static string State(
        string name, string? bio, Guid selfId, IReadOnlyList<ParticipantView> present, IReadOnlyList<ChatMessage> history,
        ChatMessage? answering, string? intent, bool owed, bool inPerson)
    {
        string Name(Guid id) => present.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefault() ?? "Someone";
        var others = present.Where(p => p.Id != selfId).Select(p => p.Name).ToList();
        var where = inPerson ? "talking in person" : others.Count == 1 ? "a private chat" : "a group chat";
        var lines = history
            .Where(m => !string.IsNullOrWhiteSpace(m.Content) && m.MessageId != answering?.MessageId)
            .TakeLast(Lines)
            .Select(m => $"{Name(m.SenderId)}: {WorldStream.Clip(m.Content, LineClip)}")
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# {name}");
        sb.AppendLine(bio ?? "(no bio)");
        sb.AppendLine();
        sb.AppendLine("# The conversation");
        sb.AppendLine($"{where}, with {(others.Count == 0 ? "nobody else" : string.Join(", ", others))}");
        sb.AppendLine();
        sb.AppendLine("# The last lines");
        sb.AppendLine(lines.Count == 0 ? "(nothing yet)" : string.Join("\n", lines));
        if (answering is { Content: { Length: > 0 } said })
        {
            sb.AppendLine();
            sb.AppendLine($"# The line {name} is answering");
            sb.AppendLine($"{Name(answering.SenderId)}: {WorldStream.Clip(said, AnsweringClip)}");
        }
        if (!string.IsNullOrWhiteSpace(intent))
        {
            sb.AppendLine();
            sb.AppendLine($"# What {name} means to get across");
            sb.AppendLine(WorldStream.Clip(intent.Trim(), IntentClip));
        }
        if (owed)
        {
            sb.AppendLine();
            sb.AppendLine($"{name} put this off earlier to answer it properly.");
        }
        return sb.ToString();
    }

    public static async Task<double?> WeighAsync(ISystemOneJudge judge, string name, string state, ILogger logger, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Budget);
            var answers = await judge.AskAsync(state, new Dictionary<string, SystemOneQuestion> { [Key] = Question(name) }, cts.Token);
            return answers.Nouls.TryGetValue(Key, out var p) ? Math.Clamp(p, 0, 1) : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogInformation("Gravity unweighed for {PersonaName}: {Error}", name, ex.Message);
            return null;
        }
    }
}
