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
        Assert.Contains("peopled out", text);
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
        Assert.Contains("What your day had for now: at the office", text);
        Assert.Contains("the day is what's going on in your life around it", text);
        Assert.Contains("'Where you are' is true and the day went differently", text);
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
    public void Analyze_DutchGrammarIsNeverWorn_OnlyItsMotifs()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var lines = new List<Utterance>
        {
            new(a, "het is maar een rinkel, dat weet ik wel"), new(b, "wat een rinkel van die tas, het is wat"),
            new(a, "dat is het met die rinkel, maar goed"), new(b, "het rinkelt niet, maar dat is een rinkel"),
            new(a, "een rinkel met een tas, wat wil je"), new(b, "van die rinkel heb ik het al gehad"),
        };
        var report = VoiceWear.Analyze(lines);
        Assert.Equal(new[] { "rinkel" }, report.WornWords);
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
        Assert.Equal(2, PartyTown.World.Apps.BoardApp.ThreadFatigue(Enumerable.Repeat(Other, 20).ToList(), Me));
        var pingPong = Enumerable.Range(0, 8).SelectMany(_ => new[] { Me, Other }).ToList();
        Assert.Equal(4, PartyTown.World.Apps.BoardApp.ThreadFatigue(pingPong, Me));
    }

    [Fact]
    public void PassedOn_OnlyUntilSomeoneWritesAgain()
    {
        var thread = new PartyTown.World.BoardThread { Id = Guid.NewGuid(), LastActivityAt = 1_000 };
        var pass = new PartyTown.World.WorldActivity { App = "board", Action = "pass", TargetId = thread.Id, Ts = 2_000 };
        Assert.True(PartyTown.World.Apps.BoardApp.PassedOn(new[] { pass }, thread));
        thread.LastActivityAt = 3_000;
        Assert.False(PartyTown.World.Apps.BoardApp.PassedOn(new[] { pass }, thread));
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

/// <summary>Coherence (ADR 0042): a persona sees what they already said or wrote, and each Day draws its own small things.</summary>
public class SaidStandsTest
{
    [Fact]
    public void BoardPost_ShowsOwnPosts_WithoutAskingForNews()
    {
        var text = PartyTown.World.Apps.BoardApp.PostUser("the radiator", null, new[] { "1 h ago: radiator finally has a landlord visit booked" }, Array.Empty<string>());
        Assert.Contains("radiator finally has a landlord visit booked", text);
        Assert.DoesNotContain("genuinely new about it", text);
    }

    [Fact]
    public void Diary_SeesWhatItAlreadyWrote_NewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new[]
        {
            new PartyTown.World.DiaryEntry { Body = "amira's train lands at 2.", CreatedAt = now.AddMinutes(-4).ToUnixTimeMilliseconds() },
            new PartyTown.World.DiaryEntry { Body = "the van on veerstraat, the cat, the saucer of milk.", CreatedAt = now.AddMinutes(-40).ToUnixTimeMilliseconds() },
            new PartyTown.World.DiaryEntry { Body = "older", CreatedAt = now.AddHours(-3).ToUnixTimeMilliseconds() },
            new PartyTown.World.DiaryEntry { Body = "oldest", CreatedAt = now.AddHours(-9).ToUnixTimeMilliseconds() },
        };

        var written = PartyTown.World.Apps.DiaryApp.OwnRecent(entries, now);
        var user = PartyTown.World.Apps.DiaryApp.WriterUser("amira", "# Right now", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), written);

        Assert.Equal(3, written.Count);
        Assert.StartsWith("4 min ago: amira's train lands at 2.", written[0]);
        Assert.Contains("What you already wrote (don't write the same entry again):", user);
        Assert.Contains("- 4 min ago: amira's train lands at 2.", user);
        Assert.DoesNotContain("oldest", user);
    }

    [Fact]
    public void Diary_WhatHappenedIsWhatDid_NotTheDayAhead()
        => Assert.Contains("anything still coming up hasn't happened yet", PartyTown.World.Apps.DiaryApp.WriterRules);

    [Fact]
    public void SmallThings_DrawsTwoDifferentDomains_StablePerPersonaAndDay()
    {
        var day = new DateTimeOffset(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);
        var people = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in people)
        {
            var line = SelfMath.SmallThings(id, day);
            Assert.Equal(line, SelfMath.SmallThings(id, day));
            var m = System.Text.RegularExpressions.Regex.Match(line, @"annoyance has to do with (.+); the good thing with (.+)\.$");
            Assert.True(m.Success, line);
            Assert.NotEqual(m.Groups[1].Value, m.Groups[2].Value);
        }
        // Independent Days spread over the domains instead of converging on one default.
        Assert.True(people.Select(id => SelfMath.SmallThings(id, day)).Distinct().Count() > 20);
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

public class ThreadCourseTest
{
    [Fact]
    public void LongThreadsRunTheirCourse()
    {
        Assert.False(PartyTown.World.Apps.BoardApp.RunItsCourse(24));
        Assert.True(PartyTown.World.Apps.BoardApp.RunItsCourse(PartyTown.World.Apps.BoardApp.MaxReplies));
    }
}

public class ValenceTest
{
    [Fact]
    public void Shift_MovesWithAppraisal_AndFadesOverHours()
    {
        Assert.Equal(0.4, SelfMath.Shift(0, 1), 3);
        Assert.Equal(-0.4, SelfMath.Shift(0, 0), 3);
        Assert.Equal(1, SelfMath.Shift(0.9, 1), 3);

        var now = DateTimeOffset.UtcNow;
        var s = new SelfState { Valence = 0.8, ValenceAt = now.AddHours(-3).ToUnixTimeMilliseconds() };
        Assert.Equal(0.4, SelfMath.Valence(s, now), 3);
        Assert.Equal(0, SelfMath.Valence(new SelfState(), now));
    }

    [Fact]
    public void Drift_RendersOnlyClearShifts()
    {
        Assert.Null(SelfRender.Drift(0.1, "#Café"));
        Assert.Equal("You're in a somewhat better mood than this morning (what happened in #Café).", SelfRender.Drift(0.3, "#Café"));
        Assert.Contains("worn on you", SelfRender.Drift(-0.6, null));
    }
}

public class AppraisalTest
{
    [Fact]
    public void Better_FromChoice()
    {
        Assert.Equal(0.5, Appraisal.Better(null));
        Assert.Equal(0.5, Appraisal.Better(new Dictionary<string, double> { ["same"] = 1 }), 3);
        Assert.Equal(0.8, Appraisal.Better(new Dictionary<string, double> { ["better"] = 0.7, ["same"] = 0.2, ["worse"] = 0.1 }), 3);
        Assert.Equal(0.25, Appraisal.Better(new Dictionary<string, double> { ["better"] = 0.1, ["same"] = 0.3, ["worse"] = 0.6 }), 3);
    }

    [Fact]
    public void Inclination_EmptyBatteryHalvesIt()
    {
        Assert.Equal(1, SelfMath.Inclination(1, 1), 3);
        Assert.Equal(0.4, SelfMath.Inclination(1, 0), 3);
    }

    [Fact]
    public void ThreeLetterWords_CanGoStale()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var lines = Enumerable.Range(0, 6).Select(i => new Utterance(i % 2 == 0 ? a : b, "the tin is on the sill")).ToList();
        Assert.Contains("tin", VoiceWear.Analyze(lines).WornWords);
        Assert.DoesNotContain("the", VoiceWear.Analyze(lines).WornWords);
    }
}

public class SignatureMarkTest
{
    [Fact]
    public void Analyze_FindsASignatureEmojiOrEmoticon()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var lines = new List<Utterance>
        {
            new(a, "Send it over. 🌿"), new(a, "Six works 🌿"), new(a, "fine"), new(a, "the seam talk 🌿"),
            new(b, "go rest :-P"), new(b, "ok"), new(b, "sandwich time"), new(b, "numbers later :-)"),
        };
        var report = VoiceWear.Analyze(lines);
        Assert.Equal("🌿", report.SignatureMark[a]);
        Assert.False(report.SignatureMark.ContainsKey(b));
        Assert.Contains(VoiceWear.RenderFor(report, a), l => l.Contains("🌿"));
    }
}

public class LifeThreadsTest
{
    [Fact]
    public void ParseThreads_SplitsOpenFromDone()
    {
        var (open, done) = SelfMath.ParseThreads("- lease renewal due Friday\n2. done: bought the tap cartridge\n* sister's wedding in November\nok");
        Assert.Equal(new[] { "lease renewal due Friday", "sister's wedding in November" }, open);
        Assert.Equal(new[] { "bought the tap cartridge" }, done);
    }

    [Fact]
    public void Presence_ShowsThreadsAndWhatGotSettled()
    {
        var day = new SelfState
        {
            Beats = SelfMath.ParseBeats("08:00 coffee"),
            Threads = new() { "lease renewal due Friday" },
            Resolved = new() { "bike light fixed" },
        };
        var text = SelfRender.Presence(day, 1, new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero), Array.Empty<string>());
        Assert.Contains("Going on in your life lately: lease renewal due Friday", text);
        Assert.Contains("Finally settled: bike light fixed", text);
    }

    [Fact]
    public void AgeThreads_CarriesAgeAcrossRewording_AndStartsNewOnesAtOne()
    {
        var before = new List<string> { "Ministry contract: the two-page note on scoring weights is due Monday", "bike needs a new rear light" };
        var now = new List<string> { "the ministry note on how the scoring weights get set, due Monday, outline started", "call the repayment office about the startup debt" };
        Assert.Equal(new[] { 4, 1 }, SelfMath.AgeThreads(before, new List<int> { 3, 1 }, now));
    }

    [Fact]
    public void AgeThreads_NeedsTwoSharedWords_AndTreatsUnagedThreadsAsOneDayOld()
    {
        var before = new List<string> { "Anne's parents arrive Sunday", "lease renewal due Friday" };
        var now = new List<string> { "Anne wants to swim on Sunday", "lease renewal due Friday, landlord still silent" };
        Assert.Equal(new[] { 1, 2 }, SelfMath.AgeThreads(before, new List<int>(), now));
    }

    [Fact]
    public void Carried_FlagsThreadsThatDragOn_AndLetsTheOldestGo()
    {
        var carried = SelfMath.CarriedThreads(new List<string> { "fresh", "stuck", "ancient" }, new List<int> { 1, SelfMath.DraggingDays, SelfMath.ThreadDays });
        Assert.Equal(2, carried.Count);
        Assert.Equal("fresh", carried[0]);
        Assert.StartsWith("stuck (dragging on for 4 days", carried[1]);
    }

    [Fact]
    public void SheetKey_ChangesWithSheetOrBio_AndIsStable()
    {
        Assert.Equal(SelfMath.SheetKey("sheet", "bio"), SelfMath.SheetKey("sheet", "bio"));
        Assert.NotEqual(SelfMath.SheetKey("sheet", "bio"), SelfMath.SheetKey("sheet!", "bio"));
        Assert.NotEqual(SelfMath.SheetKey("sheet", "bio"), SelfMath.SheetKey("sheet", "bio!"));
    }
}

public class LivedSheetsTest
{
    [Fact]
    public void Lived_OnlyForTheExactSourceItWasMadeFrom()
    {
        var id = Guid.NewGuid();
        Assert.Null(LivedSheets.Lived(id, "original sheet"));
        LivedSheets.Remember(id, LivedSheets.Hash("original sheet"), "## Who you are\nYou are Lena. You run a small practice. You like soup.");
        Assert.StartsWith("## Who you are", LivedSheets.Lived(id, "original sheet"));
        Assert.Null(LivedSheets.Lived(id, "edited sheet"));
        Assert.Equal("edited sheet", LivedSheets.For(id, "edited sheet"));
        Assert.Equal("You are Lena. You run a small practice. You like soup.", LivedSheets.Bio(LivedSheets.Lived(id, "original sheet")!));
    }
}
