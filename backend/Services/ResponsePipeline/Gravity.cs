using System.Globalization;
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

    public const string RecallKey = "recall";
    public const string Nothing = "none";
    public const double RecallTemperature = 0.8;
    public const double RecallFloor = 0.05;
    public const int MemoryClip = 240;

    public static ChoiceQuestion Recall(string name, IReadOnlyList<string> memories)
    {
        var options = new Dictionary<string, string?> { [Nothing] = $"nothing from before: {name} is just in this conversation" };
        for (var i = 0; i < memories.Count; i++)
            options[(i + 1).ToString(CultureInfo.InvariantCulture)] = WorldStream.Clip(memories[i].Trim(), MemoryClip);
        return new ChoiceQuestion($"Which of these memories, if any, is on {name}'s mind in this moment?", options);
    }

    public sealed record Weighed(double? Gravity, int? Memory, bool RecallWeighed)
    {
        public static readonly Weighed Unweighed = new(null, null, false);
    }

    public static async Task<Weighed> WeighAsync(
        ISystemOneJudge judge, string name, string state, IReadOnlyList<string> memories, ILogger logger, CancellationToken ct, Random? random = null)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Budget);
            var questions = new Dictionary<string, SystemOneQuestion> { [Key] = Question(name) };
            if (memories.Count > 0) questions[RecallKey] = Recall(name, memories);
            var answers = await judge.AskAsync(state, questions, cts.Token);
            double? gravity = answers.Nouls.TryGetValue(Key, out var p) ? Math.Clamp(p, 0, 1) : null;
            if (memories.Count == 0 || !answers.Choices.TryGetValue(RecallKey, out var dist) || dist.Count == 0)
                return new Weighed(gravity, null, false);
            answers.Damp(RecallKey, option => dist[option] < RecallFloor ? 0 : 1);
            var pick = answers.SampleChoice(RecallKey, RecallTemperature, random);
            if (pick == Nothing) return new Weighed(gravity, null, true);
            return int.TryParse(pick, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= memories.Count
                ? new Weighed(gravity, n, true)
                : new Weighed(gravity, null, false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogInformation("Gravity unweighed for {PersonaName}: {Error}", name, ex.Message);
            return Weighed.Unweighed;
        }
    }
}
