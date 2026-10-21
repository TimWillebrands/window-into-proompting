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

        Assert.Contains("Off-screen right now: lunch", text);
        Assert.DoesNotContain("Where you are", text);
        Assert.Contains("Earlier today: 07:00 burned the toast; 09:00 standup", text);
        Assert.Contains("Coming up: 18:00 groceries", text);
        Assert.Contains("heater", text);
        Assert.Contains("nearly empty", text);
        Assert.DoesNotContain("Gone stale", text);
    }

    [Fact]
    public void Presence_SituationComesFirstAndWinsOverTheDay()
    {
        var day = new SelfState { Beats = SelfMath.ParseBeats("09:00 at the office") };
        var now = new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

        var text = SelfRender.Presence(day, 1, now, Array.Empty<string>(), "You're at the Café, sitting at the window table.");

        var lines = text.Split('\n');
        Assert.Equal("Where you are: You're at the Café, sitting at the window table.", lines[2]);
        Assert.Contains("Off-screen right now: at the office", text);
        Assert.Contains("the day is what's going on in your life around it", text);
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

public class OpenersTest
{
    [Fact]
    public void ParseList_StripsBulletsAndNumbers_CapsAtFour()
    {
        var list = SelfMath.ParseList("- watched the new season of that baking show\n2. client wants dark mode\n* ok\n• why is oat milk 3 euros now\nfive\nsix things");
        Assert.Equal(new[] { "watched the new season of that baking show", "client wants dark mode", "why is oat milk 3 euros now", "five" }, list);
    }

    [Fact]
    public void Presence_ShowsOpeners()
    {
        var day = new SelfState { Beats = SelfMath.ParseBeats("08:00 coffee"), Openers = new() { "oat milk prices" } };
        var text = SelfRender.Presence(day, 1, new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero), Array.Empty<string>());
        Assert.Contains("Things you might bring up if there's a lull (or not at all): oat milk prices", text);
    }
}

public class ThreadFatigueTest
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void Fatigue_FromOwnRecentReplies_AndLength()
    {
        Assert.Equal(0, PartyTown.World.Apps.BoardApp.ThreadFatigue(new[] { Other, Me, Other }, Me));
        Assert.Equal(1, PartyTown.World.Apps.BoardApp.ThreadFatigue(new[] { Me, Other, Me, Other }, Me));
        var longThread = Enumerable.Repeat(Other, 12).ToList();
        Assert.Equal(1, PartyTown.World.Apps.BoardApp.ThreadFatigue(longThread, Me));
        longThread.AddRange(new[] { Me, Me });
        Assert.Equal(2, PartyTown.World.Apps.BoardApp.ThreadFatigue(longThread, Me));
        Assert.Equal(1, PartyTown.World.Apps.BoardApp.ThreadFatigue(Enumerable.Repeat(Other, 20).ToList(), Me) - 1);
    }
}

public class OwnRecentTest
{
    [Fact]
    public void RenderOwnRecent_ListsWhereAndWhat()
    {
        var now = DateTimeOffset.UtcNow;
        var text = VoiceWear.RenderOwnRecent(new[]
        {
            new VoiceLine(now.AddMinutes(-3).ToUnixTimeMilliseconds(), "DM Denise & Lena", "Lena", "That tin is still on his shelf.", Guid.NewGuid()),
        }, now);
        Assert.Contains("- 3 min ago, DM Denise & Lena: \"That tin is still on his shelf.\"", text);
        Assert.Contains("Don't say the same thing again", text);
        Assert.Equal(string.Empty, VoiceWear.RenderOwnRecent(Array.Empty<VoiceLine>(), now));
    }
}

public class AvailabilityTest
{
    private static SelfState Day() => new()
    {
        Beats = new()
        {
            new DayBeat { Minute = 7 * 60, Text = "coffee", Availability = 0.9 },
            new DayBeat { Minute = 9 * 60, Text = "client meeting", Availability = 0.2 },
            new DayBeat { Minute = 12 * 60, Text = "lunch", Availability = null },
        },
    };

    [Theory]
    [InlineData(6, 0.1)]
    [InlineData(8, 0.9)]
    [InlineData(10, 0.2)]
    [InlineData(13, 1.0)]
    public void Availability_FollowsTheCurrentBeat_AsleepBeforeTheDay(int hour, double expected)
        => Assert.Equal(expected, SelfMath.Availability(Day(), new DateTimeOffset(2026, 10, 21, hour, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Presence_SaysWhenTiedUp()
    {
        var text = SelfRender.Presence(Day(), 1, new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero), Array.Empty<string>());
        Assert.Contains("You're tied up with that", text);
        var free = SelfRender.Presence(Day(), 1, new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero), Array.Empty<string>());
        Assert.DoesNotContain("tied up", free);
    }
}

public class RescaleTest
{
    [Fact]
    public void Rescale_RanksWithinTheDay()
    {
        var r = SelfMath.Rescale(new[] { 0.23, 0.57, 0.40 });
        Assert.Equal(0.2, r[0], 3);
        Assert.Equal(1.0, r[1], 3);
        Assert.InRange(r[2], 0.55, 0.65);
        Assert.All(SelfMath.Rescale(new[] { 0.3, 0.32 }), p => Assert.Equal(1.0, p));
    }
}

public class VocativeRepeatTest
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void Analyze_MeasuresNameOpenersAndRepeats()
    {
        var lines = new List<Utterance>
        {
            new(A, "Denise, wednesday I'll stand next to you with the roof estimate."),
            new(A, "Denise, going to stretch my back first."),
            new(A, "wednesday I'll stand right next to you, roof estimate in my bag."),
            new(A, "soba is getting cold."),
            new(B, "Eiko: fine by me."),
            new(B, "sandwich time"),
        };
        var report = VoiceWear.Analyze(lines, new[] { "Denise Moreau", "Eiko Tanaka" });

        Assert.Equal(0.5, report.VocativeRate[A], 3);
        Assert.Equal(0.25, report.RepeatRate[A], 3);
        Assert.Equal(0.5, report.VocativeRate[B], 3);
        Assert.Equal(0, report.RepeatRate[B], 3);
        Assert.Contains(VoiceWear.RenderFor(report, A), l => l.Contains("open with someone's name"));
    }
}

public class BeatRangeTest
{
    [Fact]
    public void ParseBeats_DropsARangesEndTime()
    {
        var beats = SelfMath.ParseBeats("10:00 12:30 Grind through a client's integration\n13:00–14:00 lunch");
        Assert.Equal("Grind through a client's integration", beats[0].Text);
        Assert.Equal("lunch", beats[1].Text);
    }
}
