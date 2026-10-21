using PartyTown.Services.Memory;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.World;
using PartyTown.World.Apps;

namespace BackendTest;

/// <summary>Pure pieces of the World (ADR 0018): keys, formatting, Board→capture mapping, DM urge.</summary>
public class WorldPureTest
{
    [Fact]
    public void LifeKey_RoundTrips()
    {
        var party = Guid.NewGuid();
        var persona = Guid.NewGuid();
        Assert.Equal((party, persona), WorldKeys.ParseLife(WorldKeys.Life(party, persona)));
    }

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(2 * 86400, "2 d ago")]
    public void Ago_BucketsElapsedTime(int secondsAgo, string expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, WorldStream.Ago(now.AddSeconds(-secondsAgo).ToUnixTimeMilliseconds(), now));
    }

    [Fact]
    public void Clip_FlattensAndTruncates()
    {
        Assert.Equal("a b…", WorldStream.Clip("a\nb c d", 3));
        Assert.Equal("", WorldStream.Clip("  ", 10));
    }

    [Fact]
    public void BoardMoment_OpFirst_RepliesInOrder_MarkedAtLatest()
    {
        var op = Guid.NewGuid();
        var replier = Guid.NewGuid();
        var thread = new BoardThread
        {
            Id = Guid.NewGuid(), AuthorId = op, Title = "Lisp?", Body = "Is it elegant?",
            Replies = new List<BoardReply>
            {
                new() { AuthorId = replier, Body = "Yes." },
                new() { AuthorId = op, Body = "Knew it." },
            },
        };

        var moment = BoardApp.MomentOf(Guid.NewGuid(), thread,
            ids => ids.Select(id => new ParticipantView(id, "p", DriverKind.LLM)).ToList());

        Assert.Equal(new[] { op, replier, op }, moment.Lines.Select(l => l.SenderId));
        Assert.Contains("Lisp?", moment.Lines[0].Text);
        Assert.Equal(2, moment.MarkIndex);
        Assert.Equal(MomentSources.Board, moment.Source);
        Assert.Equal(thread.Id, moment.ThreadId);
        Assert.Equal(new[] { op, replier }, moment.Present.Select(p => p.Id));
    }

    [Fact]
    public void FromMessages_MarksByMessageId_SourceFollowsKind()
    {
        var a = Guid.NewGuid();
        var msgs = new List<ChatMessage>
        {
            new() { MessageId = 7, SenderId = a, Content = "later" },
            new() { MessageId = 3, SenderId = a, Content = "first" },
        };

        var moment = Moment.FromMessages(Guid.NewGuid(), MomentSources.OfKind(ChatGroupKinds.Direct), Guid.NewGuid(), msgs, 7,
            Array.Empty<ParticipantView>());

        Assert.Equal(new[] { "first", "later" }, moment.Lines.Select(l => l.Text));
        Assert.Equal(1, moment.MarkIndex);
        Assert.Equal(MomentSources.Messenger, moment.Source);
        Assert.Equal(MomentSources.Places, MomentSources.OfKind(ChatGroupKinds.Place));
        Assert.Throws<KeyNotFoundException>(() => Moment.FromMessages(Guid.NewGuid(), "rooms", Guid.NewGuid(), msgs, 99,
            Array.Empty<ParticipantView>()));
    }

    [Fact]
    public void DirectThread_EveryLineCountsAsMention()
    {
        var self = new SelfView(Guid.NewGuid(), "Denise", DriverKind.LLM, null, null, 0.5, 0.5);
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = Guid.NewGuid(), SenderType = "user", Content = "hey there" },
        };

        Assert.Equal(0, UrgeMath.CalculateResponseUrge(self, history, 0).MentionScore);
        Assert.Equal(1.0, UrgeMath.CalculateResponseUrge(self, history, 0, directlyAddressed: true).MentionScore);
    }
}

public class SpeechCleanupTest
{
    [Theory]
    [InlineData("<|channel|>private<|message|> Denise: hey... the lantern", "hey... the lantern")]
    [InlineData("**Denise:** oui", "oui")]
    [InlineData("Denise said hi to Pieter: fine", "Denise said hi to Pieter: fine")]
    [InlineData("plain line", "plain line")]
    [InlineData("Okay. Going. #test:  The hour the grid died", "Okay. Going. The hour the grid died")]
    [InlineData("we use #hashtags here", "we use #hashtags here")]
    public void Clean_StripsTemplateTokensAndSelfLabel(string raw, string expected)
        => Assert.Equal(expected, PartyTown.Services.ResponsePipeline.SpeechCleanup.Clean(raw, "Denise"));
}

public class WorldLlmJsonTest
{
    [Fact]
    public void ParseLastWins_ToleratesDuplicateKeys()
    {
        var obj = PartyTown.World.WorldLlm.ParseLastWins("""{"intent":"a","choice":"1","intent":"b"}""");
        Assert.Equal("b", obj!["intent"]!.GetValue<string>());
        Assert.Equal("1", obj["choice"]!.GetValue<string>());
    }
}

public class StageDirectionTest
{
    [Theory]
    [InlineData("*sets the phone down on the table*  Denise, you were right.", "Denise, you were right.")]
    [InlineData("*sigh* fine.", "*sigh* fine.")]
    [InlineData("Not happy. Something better — *unarmed.* That's the word.", "Not happy. Something better — *unarmed.* That's the word.")]
    [InlineData("*I really mean it this time*", "*I really mean it this time*")]
    [InlineData("ok *leans back in the chair* so what now", "ok so what now")]
    [InlineData("*long pause before typing*", "*long pause before typing*")]
    public void StripStageDirections_DropsNarrationKeepsEmphasis(string raw, string expected)
        => Assert.Equal(expected, SpeechCleanup.StripStageDirections(raw));
}
