using System.Diagnostics;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.SystemOne;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Stop-signal salience (ADR 0001): is a new message worth interrupting an in-flight reply for? Pure weighing,
/// so System One answers it; any failure lets the draft ride.
/// </summary>
public sealed class PersonaSalienceService(ISystemOneJudge judge, ILogger logger)
{
    private static readonly IReadOnlyDictionary<string, string?> Kinds = new Dictionary<string, string?>
    {
        ["redirect"] = "the new message changes the topic so the reply in progress would seem odd",
        ["contradict"] = "the new message contradicts what the reply in progress assumes",
        ["tangent"] = "on-topic, but the reply in progress still works",
        ["irrelevant"] = "unrelated to the reply in progress",
    };

    /// <summary>0 (let it ride) to 1 (finishing now would look out of touch).</summary>
    public async Task<SalienceScore> ScoreAsync(
        SelfView self,
        string inFlightGutReaction,
        string inFlightWouldSay,
        string inFlightGeneratedSoFar,
        ChatMessage newMessage,
        string newMessageSenderName,
        CancellationToken ct)
    {
        using var span = Tracing.Persona.StartActivity("persona.salience", ActivityKind.Internal);
        span?.SetTag("persona.id", self.Id);
        span?.SetTag("persona.name", self.Name);
        span?.SetTag("new_message.sender", newMessageSenderName);

        try
        {
            var state = State(self.Name, inFlightGutReaction, inFlightWouldSay, inFlightGeneratedSoFar,
                newMessage.Content ?? string.Empty, newMessageSenderName);
            var answers = await judge.For(self.Id).AskAsync(state, new Dictionary<string, SystemOneQuestion>
            {
                ["interrupt"] = new NoulQuestion(
                    $"If {self.Name} finishes and sends the in-flight message unchanged, it will look out of touch, wrong or rude given the new message."),
                ["kind"] = new ChoiceQuestion("How does the new message relate to the in-flight message?", Kinds),
            }, ct);
            if (!answers.Nouls.ContainsKey("interrupt"))
            {
                span?.SetTag("salience.fallback", "no-answer");
                return SalienceScore.LetItRide;
            }
            var kind = answers.Choices.TryGetValue("kind", out var dist) && dist.Count > 0
                ? dist.MaxBy(kv => kv.Value).Key
                : "tangent";
            var scored = new SalienceScore(Math.Clamp(answers.Noul("interrupt", 0), 0, 1), kind);
            span?.SetTag("salience.value", scored.Value);
            span?.SetTag("salience.kind", scored.Kind);
            return scored;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Persona {PersonaName} salience failed; letting the draft ride", self.Name);
            span?.SetTag("salience.fallback", "error");
            return SalienceScore.LetItRide;
        }
    }

    private static string State(
        string personaName,
        string gutReaction,
        string wouldSay,
        string generatedSoFar,
        string newMessageContent,
        string newMessageSender) => $"""
# In-flight utterance from {personaName}
Gut reaction: {Trim(gutReaction)}
Planned message: {Trim(wouldSay)}
Generated so far: {Trim(generatedSoFar)}

# New message just arrived
From: {newMessageSender}
Content: {Trim(newMessageContent)}
""";

    private static string Trim(string s) =>
        string.IsNullOrWhiteSpace(s) ? "(empty)" : s.Trim();
}

/// <summary>Interruption salience: <see cref="Value"/> drives the race, <see cref="Kind"/> is telemetry.</summary>
public readonly record struct SalienceScore(double Value, string Kind)
{
    /// <summary>Any failure: don't cancel.</summary>
    public static SalienceScore LetItRide { get; } = new(0.0, "irrelevant");
}
