using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Lived self (ADR 0021): offscreen day, social battery, stale-word detection.</summary>
public class LivedSelfTest
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    [Fact]
    public void ParseBeats_ReadsLooseTimeFormats_AndSorts()
    {
        var beats = SelfMath.ParseBeats("- 12:30 — lunch at desk\n07.45 alarm, snooze twice\n9h00: standup runs long\nnot a beat\n25:00 nope");
        Assert.Equal(new[] { 465, 540, 750 }, beats.Select(b => b.Minute));
        Assert.Equal("alarm, snooze twice", beats[0].Text);
    }

    [Fact]
    public void Battery_DrainsWithTalk_MoreForQuietPeople_AndRecharges()
    {
        var quiet = SelfMath.Drain(1, chattiness: 0);
        var chatty = SelfMath.Drain(1, chattiness: 1);
        Assert.True(quiet < chatty);

        var now = DateTimeOffset.UtcNow;
        var state = new SelfState { Battery = 0.2, BatteryAt = now.AddHours(-2).ToUnixTimeMilliseconds() };
        Assert.Equal(0.7, SelfMath.Battery(state, now), 2);
        Assert.Equal(1, SelfMath.Battery(new SelfState(), now));
    }

    [Fact]
    public void Presence_PlacesNowInTheDay()
    {
        var day = new SelfState
        {
            Mood = "tired, a bit flat",
            Energy = "low",
            Preoccupation = "The heater still isn't fixed.",
            Beats = SelfMath.ParseBeats("07:00 burned the toast\n09:00 standup\n13:00 lunch\n18:00 groceries"),
        };
        var now = new DateTimeOffset(2026, 10, 21, 14, 10, 0, TimeSpan.Zero);
        var text = SelfRender.Presence(day, 0.2, now, Array.Empty<string>());

        Assert.Contains("Where your day is at: lunch", text);
        Assert.Contains("Earlier today: 07:00 burned the toast; 09:00 standup", text);
        Assert.Contains("Coming up: 18:00 groceries", text);
        Assert.Contains("heater", text);
        Assert.Contains("nearly empty", text);
        Assert.DoesNotContain("Gone stale", text);
    }

    [Fact]
    public void Presence_WithoutDay_OnlyBatteryAndWear()
    {
        Assert.Equal(string.Empty, SelfRender.Presence(null, 1, DateTimeOffset.Now, Array.Empty<string>()));
        var text = SelfRender.Presence(null, 1, DateTimeOffset.Now, new[] { "stale" });
        Assert.DoesNotContain("Right now", text);
        Assert.Contains("# Gone stale\n- stale", text);
    }

    [Fact]
    public void Analyze_FindsSharedMonoculture_NotOneSpeakersHabit()
    {
        var lines = new List<Utterance>
        {
            new(A, "*sets the cup down* The lantern was never about light, Denise."),
            new(B, "a load-bearing silence, like the lantern"),
            new(C, "the lantern again? load-bearing walls everywhere"),
            new(A, "a lantern is a load-bearing seam you keep lit"),
            new(B, "that lantern keeps coming up. load-bearing, honestly"),
            new(A, "*leans back* load-bearing is the word"),
            new(C, "pizza tonight? pizza is always the answer. pizza"),
            new(C, "pizza again"),
            new(C, "fine, pizza"),
        };
        var report = VoiceWear.Analyze(lines, new[] { "Denise" });

        Assert.Equal("lantern", report.WornWords[0]);
        Assert.Contains("load-bearing", report.WornWords);
        Assert.DoesNotContain("pizza", report.WornWords);
        Assert.DoesNotContain("denise", report.WornWords);
        Assert.Contains("pizza", report.OwnWords[C]);
        Assert.Equal(2 / 3.0, report.StageDirectionRate[A], 3);
    }

    [Fact]
    public void RenderFor_SplitsSharedFromOwn_AndFlagsStageDirections()
    {
        var report = new VoiceWearReport
        {
            WornWords = new() { "lantern", "seam" },
            OwnWords = new() { [A] = new() { "seam" } },
            StageDirectionRate = new() { [A] = 0.8, [B] = 0.1 },
            AverageWords = new() { [A] = 20 },
        };
        var lines = VoiceWear.RenderFor(report, A);
        Assert.Contains(lines, l => l.Contains("lantern") && !l.Contains("seam"));
        Assert.Contains(lines, l => l.StartsWith("You personally") && l.Contains("seam"));
        Assert.Contains(lines, l => l.Contains("*action*"));
        Assert.DoesNotContain(VoiceWear.RenderFor(report, B), l => l.Contains("*action*"));
    }
}

public class UtteranceRhythmTest
{
    [Theory]
    [InlineData(5, 0.1, PartyTown.Services.ResponsePipeline.UtteranceRhythm.Length.Short)]
    [InlineData(5, 0.8, PartyTown.Services.ResponsePipeline.UtteranceRhythm.Length.Medium)]
    [InlineData(5, 0.97, PartyTown.Services.ResponsePipeline.UtteranceRhythm.Length.Long)]
    [InlineData(80, 0.5, PartyTown.Services.ResponsePipeline.UtteranceRhythm.Length.Medium)]
    [InlineData(80, 0.8, PartyTown.Services.ResponsePipeline.UtteranceRhythm.Length.Long)]
    public void Pick_TracksTheOtherPersonsLength(int lastWords, double roll, PartyTown.Services.ResponsePipeline.UtteranceRhythm.Length expected)
        => Assert.Equal(expected, PartyTown.Services.ResponsePipeline.UtteranceRhythm.Pick(lastWords, roll));

    [Fact]
    public void Pick_MostlyShortAfterAShortLine()
    {
        var shortShare = Enumerable.Range(0, 100).Count(i =>
            PartyTown.Services.ResponsePipeline.UtteranceRhythm.Pick(6, i / 100.0) == PartyTown.Services.ResponsePipeline.UtteranceRhythm.Length.Short);
        Assert.InRange(shortShare, 60, 80);
    }
}
