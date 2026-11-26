using PartyTown.Grains;
using PartyTown.World;
using PartyTown.World.Desires;
using PartyTown.World.Self;

namespace BackendTest;

public class TakingStockTest
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 16, 0, 0, TimeSpan.Zero);

    private static WorldActivity Did(string action, double hoursAgo, string summary = "did a thing", string? thought = null, string? affordance = null)
        => new() { App = "life", Action = action, Summary = summary, Thought = thought, Affordance = affordance, Ts = Now.AddHours(-hoursAgo).ToUnixTimeMilliseconds() };

    [Theory]
    [InlineData(0.0, 1.0, 1.0)]
    [InlineData(0.8, 1.0, 0.04)]
    [InlineData(0.5, 1.0, 0.25)]
    [InlineData(0.0, 0.5, 0.25)]
    [InlineData(0.5, 0.5, 0.0625)]
    [InlineData(1.0, 1.0, 0.0)]
    [InlineData(0.3, 0.0, 0.0)]
    public void Chance_IsWantOfGripSquared_TimesRipenessSquared(double grip, double ripe, double chance)
        => Assert.Equal(chance, TakingStock.Chance(grip, ripe), 6);

    [Fact]
    public void Grip_IsAskedAsItsLoss()
    {
        Assert.Equal("Mara keeps doing the same thing and it isn't getting them anywhere.", ((PartyTown.Services.SystemOne.NoulQuestion)Grip.Question("Mara")).Instructions);
        Assert.Equal(0.3, Grip.FromLoss(0.7), 6);
        Assert.Equal(1, Grip.FromLoss(-0.2), 6);
        Assert.Equal(0, Grip.FromLoss(1.4), 6);
    }

    [Theory]
    [InlineData(0, 1.5, 0.5)]
    [InlineData(0, 3, 1)]
    [InlineData(0, 9, 1)]
    [InlineData(1, 3, 0.5)]
    [InlineData(2, 6, 0.5)]
    [InlineData(3, 6, 0.5)]
    [InlineData(9, 6, 0.5)]
    public void Ripe_ComesBackOverHours_AndEachStockTakeThatCameToNothingDoublesTheWait(int fruitless, double hours, double ripe)
        => Assert.Equal(ripe, TakingStock.Ripe(TimeSpan.FromHours(hours), fruitless), 6);

    [Fact]
    public void Window_IsCappedAtTwelveHours()
    {
        Assert.Equal(TakingStock.Window, TakingStock.WindowAfter(0));
        Assert.Equal(TimeSpan.FromHours(6), TakingStock.WindowAfter(1));
        Assert.Equal(TakingStock.LongestWindow, TakingStock.WindowAfter(2));
        Assert.Equal(TakingStock.LongestWindow, TakingStock.WindowAfter(40));
    }

    [Fact]
    public void Ripeness_NeedsThreeThingsLived_AndRestingLookingWritingDoNotCount()
    {
        var self = new SelfState { StockAt = Now.AddHours(-4).ToUnixTimeMilliseconds() };
        var own = new[]
        {
            Did("post", 3.5), Did("rest", 3), Did(PersonaLifeGrain.LookAction, 2.5), Did("diary", 2), Did("dm", 1.5),
            Did("fizzle", 1), Did(Lack.Action, 1), Did(OwnHappenings.Action, 1),
        };
        Assert.Equal(0, TakingStock.Ripeness(self, TakingStock.Since(own, self.StockAt, Now), Now));

        var more = own.Append(Did("say", 0.5)).ToList();
        Assert.Equal(1, TakingStock.Ripeness(self, TakingStock.Since(more, self.StockAt, Now), Now));
        Assert.Equal(0, TakingStock.Ripeness(null, TakingStock.Since(more, self.StockAt, Now), Now));
    }

    [Fact]
    public void Ripeness_CountsOnlyWhatCameSinceTheLastStockTake()
    {
        var self = new SelfState { StockAt = Now.AddHours(-1.5).ToUnixTimeMilliseconds() };
        var own = new[] { Did("post", 5), Did("dm", 4), Did("say", 3), Did(TakingStock.Action, 1.5), Did("post", 1), Did("dm", 0.5) };

        Assert.Equal(2, TakingStock.Lived(TakingStock.Since(own, self.StockAt, Now)));
        Assert.Equal(0, TakingStock.Ripeness(self, TakingStock.Since(own, self.StockAt, Now), Now));
        var third = own.Append(Did("say", 0.2)).ToList();
        Assert.Equal(0.5, TakingStock.Ripeness(self, TakingStock.Since(third, self.StockAt, Now), Now), 6);
    }

    [Fact]
    public void Ripeness_NeverTakenStock_RunsFromTheirFirstDeedOfTheLastDay()
    {
        var own = new[]
        {
            new WorldActivity { Action = "post", Summary = "x", Ts = Now.AddHours(-25).ToUnixTimeMilliseconds() },
            new WorldActivity { Action = "post", Summary = "x", Ts = Now.AddMinutes(-90).ToUnixTimeMilliseconds() },
            new WorldActivity { Action = "dm", Summary = "x", Ts = Now.AddMinutes(-60).ToUnixTimeMilliseconds() },
            new WorldActivity { Action = "say", Summary = "x", Ts = Now.AddMinutes(-30).ToUnixTimeMilliseconds() },
        };
        var deeds = TakingStock.Since(own, 0, Now);

        Assert.Equal(3, deeds.Count);
        Assert.Equal(0.5, TakingStock.Ripeness(new SelfState(), deeds, Now), 6);
    }

    [Fact]
    public void Read_NotesAreTheText_AndTheUpshotIsTheLastSoLine()
    {
        var text = "I asked it twenty times and it kept saying the same.\nSo: maybe I ask Justin.\n\nIt isn't getting me anywhere.\n**So:** I'll stop asking the CDU and go see Pieter instead.";
        var stock = TakingStock.Read(text);

        Assert.False(stock.Nothing);
        Assert.Equal(text, stock.Notes);
        Assert.Equal("I'll stop asking the CDU and go see Pieter instead.", stock.Upshot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  none ")]
    [InlineData("None.")]
    [InlineData("It's been an ordinary afternoon and it's going fine.\nSo: nothing")]
    [InlineData("It's going fine.\nSo: Nothing.")]
    [InlineData("It's going fine.\nSo: none")]
    [InlineData("It's going fine.\nSo: nothing needs to change.")]
    public void Read_NoneOrSoNothing_CameToNothing(string? text)
    {
        var stock = TakingStock.Read(text);

        Assert.True(stock.Nothing);
        Assert.Null(stock.Upshot);
    }

    [Fact]
    public void Read_WithoutASoLine_KeepsTheNotes_ButNoUpshot()
    {
        var stock = TakingStock.Read("I keep asking it the same thing. I should try something else, but I don't know what yet.");

        Assert.False(stock.Nothing);
        Assert.NotNull(stock.Notes);
        Assert.Null(stock.Upshot);
    }

    [Fact]
    public void Read_TheUpshotIsItsFirstSentence_SoAPlanAddedAfterItDoesNotStand()
    {
        var stock = TakingStock.Read("I kept asking.\n\nSo: I'll stop chasing loose threads one question at a time. And I'll answer Justin and Fleur before anything else.");

        Assert.Equal("I'll stop chasing loose threads one question at a time.", stock.Upshot);
        Assert.Equal("I'm done chasing Dan by message; it isn't getting me the figures.", TakingStock.Sentence("I'm done chasing Dan by message; it isn't getting me the figures."));
        Assert.Equal("Stop. Then go on as before", TakingStock.Sentence("Stop. Then go on as before"));
    }

    [Fact]
    public void Read_ClipsNotesAndUpshot()
    {
        var stock = TakingStock.Read($"{new string('a', 3000)}\nSo: {new string('b', 600)}");

        Assert.True(stock.Notes!.Length <= TakingStock.NotesMax + 1);
        Assert.True(stock.Upshot!.Length <= TakingStock.UpshotMax + 1);
    }

    [Fact]
    public void Record_SomethingResetsTheWait_NothingDoublesIt_AndKeepsWhatCameBefore()
    {
        var s = new SelfState();
        var at = Now.ToUnixTimeMilliseconds();

        TakingStock.Record(s, null, null, at);
        TakingStock.Record(s, null, null, at + 1);
        Assert.Equal(2, s.StockFruitless);
        Assert.Equal(at + 1, s.StockAt);
        Assert.Null(s.Upshot);

        TakingStock.Record(s, "notes", "go see Pieter", at + 2);
        Assert.Equal(0, s.StockFruitless);
        Assert.Equal("go see Pieter", s.Upshot);
        Assert.Equal(at + 2, s.StockNotesAt);

        TakingStock.Record(s, null, null, at + 3);
        Assert.Equal(1, s.StockFruitless);
        Assert.Equal("go see Pieter", s.Upshot);
        Assert.Equal("notes", s.StockNotes);
        Assert.Equal(at + 2, s.StockNotesAt);
        Assert.Equal(at + 3, s.StockAt);
    }

    [Fact]
    public void Deeds_OldestFirst_WithWhatTheyWereThinking_LeavingOutWhatIsNotADeed()
    {
        var own = new[]
        {
            Did("dm", 0.5, "asked the CDU about the boat", "maybe it knows this time"),
            Did("rest", 1, "thought better of it (Ask the CDU)", "I asked it already", affordance: "cdu|ask"),
            Did("fizzle", 1.2, "meant to: walk to the Café"),
            Did("error", 1.3, "tried to: post"),
            Did(Lack.Action, 1.4, "sell a painting"),
            Did(OwnHappenings.Action, 1.5, "got a call from their sister"),
            Did(TakingStock.Action, 1.6, TakingStock.Summary, "an old upshot"),
            Did("post", 2, "posted about the tram"),
        };

        var text = TakingStock.Deeds(own, Now);

        Assert.Equal(
            "# What you've been doing, oldest first\n"
            + "- 2 h ago: post posted about the tram\n"
            + "- 1 h ago: rest thought better of it (Ask the CDU)\n"
            + "- 30 min ago: dm asked the CDU about the boat (you were thinking: maybe it knows this time)",
            text);
    }

    [Fact]
    public void Deeds_KeepsTheLatestFifteen()
    {
        var own = Enumerable.Range(1, 20).Select(i => Did("post", i / 10.0, $"post {i}")).ToList();

        var lines = TakingStock.Deeds(own, Now).Split('\n').Skip(1).ToList();

        Assert.Equal(TakingStock.MaxDeeds, lines.Count);
        Assert.EndsWith("post 15", lines[0]);
        Assert.EndsWith("post 1", lines[^1]);
    }

    [Fact]
    public void Presence_SaysWhatTheyCameTo_ForEightHours()
    {
        var day = new SelfState
        {
            Beats = SelfMath.ParseBeats("08:00 coffee\n21:00 dishes"),
            Upshot = "I'll stop asking the CDU and go see Pieter.",
            StockNotes = "It isn't getting me anywhere.",
            StockNotesAt = Now.ToUnixTimeMilliseconds(),
        };

        Assert.Contains("What you came to when you last stopped to think it over (1 h ago): I'll stop asking the CDU and go see Pieter.",
            SelfRender.Presence(day, 1, Now.AddHours(1), new List<string>()));
        Assert.Contains("(7 h ago)", SelfRender.Presence(day, 1, Now.AddHours(7.5), new List<string>()));
        Assert.DoesNotContain("What you came to", SelfRender.Presence(day, 1, Now.AddHours(8), new List<string>()));
        Assert.DoesNotContain("It isn't getting me anywhere", SelfRender.Presence(day, 1, Now.AddHours(1), new List<string>()));
    }

    [Fact]
    public void Notes_ShowInLifeForTwoHours()
    {
        var day = new SelfState { StockNotes = "It isn't getting me anywhere.", Upshot = "go see Pieter", StockNotesAt = Now.ToUnixTimeMilliseconds() };
        var none = Array.Empty<WorldActivity>();

        Assert.Equal("# What you worked out when you stopped to think, 30 min ago\nIt isn't getting me anywhere.", TakingStock.Notes(day, none, Now.AddMinutes(30)));
        Assert.Null(TakingStock.Notes(day, none, Now.AddHours(2)));
        Assert.Null(TakingStock.Notes(null, none, Now));
    }

    [Fact]
    public void Notes_AreForTheNextAct_AndLeaveOnceOneThingIsLived_WhileTheUpshotStays()
    {
        var at = Now.AddMinutes(-30);
        var day = new SelfState { Beats = SelfMath.ParseBeats("08:00 coffee\n21:00 dishes"), StockNotes = "It isn't getting me anywhere.", Upshot = "go see Pieter", StockNotesAt = at.ToUnixTimeMilliseconds() };
        WorldActivity After(string action, int minutes) => new() { Action = action, Summary = "x", Ts = at.AddMinutes(minutes).ToUnixTimeMilliseconds() };
        var before = new[] { new WorldActivity { Action = "post", Summary = "x", Ts = at.AddMinutes(-10).ToUnixTimeMilliseconds() }, After(TakingStock.Action, 0) };

        Assert.NotNull(TakingStock.Notes(day, before, Now));
        Assert.NotNull(TakingStock.Notes(day, before.Append(After("rest", 5)).Append(After(PersonaLifeGrain.LookAction, 6)).Append(After("fizzle", 7)), Now));
        Assert.Null(TakingStock.Notes(day, before.Append(After("dm", 10)), Now));
        Assert.Contains("go see Pieter", SelfRender.Presence(day, 1, Now, new List<string>()));
    }

    [Theory]
    [InlineData("I'll stop messaging Dan and call him once with a firm time.", "I'll stop messaging Dan and call him once, with a firm time.", true)]
    [InlineData("Ik zeg Denise vanavond eerlijk waar de commits staan.", "ik zeg denise vanavond eerlijk waar de commits staan", true)]
    [InlineData("I'll stop messaging Dan and phone him directly, and if he hasn't sent the figures by the end of the call, I'll take it to the partner before 18:00.", "I'll stop messaging Dan and call him now, and if he can't give me a firm time, I'll take it to a partner before 18:00.", true)]
    [InlineData("I'll stop messaging Dan and call him now, and if he can't give me a firm time, I'll take it to a partner before 18:00.", "I stop sending Dan reminders, ask him once, directly, for a time he'll have the figures to me today, and if he can't give one, I take it to the partner before Thursday.", true)]
    [InlineData("I'll stop messaging Dan and call him once with a firm time.", "I'll tell Justin what I found and answer Fleur before five.", false)]
    [InlineData("I'll tell Denise honestly that I missed 17:15 and send her a time I can keep, not another promise.", "ik stuur Denise nu een eerlijk bericht dat de commits er vanavond niet meer komen.", false)]
    [InlineData("go see Pieter", null, false)]
    [InlineData(null, "go see Pieter", false)]
    public void Same_IsWordOverlapAtTheBar(string? standing, string? upshot, bool same)
        => Assert.Equal(same, TakingStock.Same(standing, upshot));

    [Fact]
    public void Against_AStockTakeThatLandsWhereTheLastOneDid_CameToNothing()
    {
        var at = Now.AddHours(-3);
        var day = new SelfState { StockNotes = "old notes", Upshot = "I'll stop messaging Dan and call him once with a firm time.", StockNotesAt = at.ToUnixTimeMilliseconds(), StockAt = at.ToUnixTimeMilliseconds() };
        var again = new TakingStock.Stock("new notes", "I'll stop messaging Dan, and call him once with a firm time.");
        var other = new TakingStock.Stock("new notes", "I'll get on with the clauses I can check without his figures.");

        var stock = TakingStock.Against(again, day, Now);
        Assert.True(stock.Nothing);
        Assert.Null(stock.Upshot);
        Assert.Same(other, TakingStock.Against(other, day, Now));
        Assert.Same(again, TakingStock.Against(again, day, Now.AddHours(6)));
        Assert.Same(again, TakingStock.Against(again, null, Now));

        TakingStock.Record(day, stock.Notes, stock.Upshot, Now.ToUnixTimeMilliseconds());
        Assert.Equal(1, day.StockFruitless);
        Assert.Equal("old notes", day.StockNotes);
    }

    [Fact]
    public void Material_EndsWithWhatTheyCouldDoFromHere()
    {
        var text = TakingStock.Material("# The world right now\n(world)\n", new[] { Did("post", 1, "posted about the tram") }, new[] { "Write a new post on Partyboard", "Go to the Café" }, "Do nothing for now", Now);

        Assert.EndsWith("# What you could do from here\n- Write a new post on Partyboard\n- Go to the Café\n- Do nothing for now", text);
        Assert.True(text.IndexOf("posted about the tram", StringComparison.Ordinal) < text.IndexOf("# What you could do from here", StringComparison.Ordinal));
        Assert.DoesNotContain("1.", text);
    }

    [Fact]
    public void May_OnlyWhenNothingAsksAnythingOfThem()
    {
        var calm = new[] { new PartyTown.World.Apps.Affordance("board", "Write a new post", 2), new PartyTown.World.Apps.Affordance("diary", "Write in your diary", 1) };
        var pressing = calm.Append(new PartyTown.World.Apps.Affordance("messenger", "Reply to Sparky in Messenger", TakingStock.Pressing)).ToList();

        Assert.True(TakingStock.May(false, null, null, 0.5, calm));
        Assert.False(TakingStock.May(false, null, null, 0.5, pressing));
        Assert.False(TakingStock.May(true, null, null, 0.5, calm));
        Assert.False(TakingStock.May(false, "Sparky waved at you", null, 0.5, calm));
        Assert.False(TakingStock.May(false, null, Bio.Food, 0.5, calm));
        Assert.False(TakingStock.May(false, null, null, 0, calm));
        var evening = calm.Append(new PartyTown.World.Apps.Affordance("places", "Turn in early", 3, Meets: Bio.Rest)).Append(new PartyTown.World.Apps.Affordance("places", "Eat something", 4, Meets: Bio.Food)).ToList();
        Assert.True(TakingStock.May(false, null, null, 0.5, evening));
    }

    [Fact]
    public void ATakingStock_IsNotSomethingLived_ForTheDiary()
        => Assert.Equal(1, PartyTown.World.Apps.DiaryApp.Lived(new[] { Did(TakingStock.Action, 1), Did("post", 1) }, null));
}

public partial class WorldLifeTest
{
    [Fact]
    public async Task LifeTurn_ADrawnStockTake_IsTheTurn_LeavesItsUpshot_AndPicksNoOption()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var start = DateTimeOffset.UtcNow.AddHours(-3).AddMinutes(-10);
        WorldActivity Did(int minutes, string app, string action, string summary, string? thought = null, string? affordance = null)
            => new() { PersonaId = denise, PersonaName = "Denise", App = app, Action = action, Summary = summary, Thought = thought, Affordance = affordance, Ts = start.AddMinutes(minutes).ToUnixTimeMilliseconds() };
        foreach (var a in new[]
        {
            Did(0, "board", "post", "posted about the tram timetable", "maybe someone knows"),
            Did(20, "messenger", "dm", "asked Pieter about the tram timetable", "he rides it every day"),
            Did(40, "places", "fizzle", "meant to: walk to the tram stop"),
            Did(60, "life", "rest", "thought better of it (Ask Pieter again)", "I already asked him", "messenger|dm"),
            Did(80, "board", "reply", "replied about the tram timetable again"),
        })
            await clock.RecordAsync(a);
        ScriptedLlm.ChooseOptionContaining = "Write a new post";
        ScriptedLlm.Nouls = key => key == Grip.Key ? 1 : null;
        ScriptedLlm.Text = (system, user) => system.Contains("You've stopped for a moment")
            ? "I keep circling the tram timetable and nobody has it.\nSo: I'll stop asking about the timetable and walk to the stop to read it myself."
            : null;
        try
        {
            var activity = await _fx.GrainFactory.GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, denise)).LiveAsync(null);

            Assert.Equal("life", activity.App);
            Assert.Equal(TakingStock.Action, activity.Action);
            Assert.Equal(TakingStock.Summary, activity.Summary);
            Assert.Equal("I'll stop asking about the timetable and walk to the stop to read it myself.", activity.Thought);
            Assert.Empty(await _fx.GrainFactory.GetGrain<IBoardGrain>(partyId).GetThreadsAsync());
            var self = await _fx.GrainFactory.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(partyId, denise)).GetStateAsync();
            Assert.Equal(activity.Thought, self.Upshot);
            Assert.StartsWith("I keep circling the tram timetable", self.StockNotes);
            Assert.True(self.StockAt > start.ToUnixTimeMilliseconds());
            Assert.Equal(0, self.StockFruitless);
            var looked = Assert.Single(ScriptedLlm.Prompts, p => p.Contains("# What you've been doing, oldest first") && p.Contains("tram timetable"));
            var deeds = looked[looked.IndexOf("# What you've been doing, oldest first", StringComparison.Ordinal)..];
            Assert.Contains("post posted about the tram timetable (you were thinking: maybe someone knows)", deeds);
            Assert.Contains("rest thought better of it (Ask Pieter again)\n", deeds);
            Assert.DoesNotContain("I already asked him", deeds);
            Assert.DoesNotContain("walk to the tram stop", deeds);
            Assert.True(deeds.IndexOf("posted about", StringComparison.Ordinal) < deeds.IndexOf("replied about", StringComparison.Ordinal));
            var offer = deeds[deeds.IndexOf("# What you could do from here", StringComparison.Ordinal)..];
            Assert.Contains("\n- Write a new post", offer);
            Assert.Contains("\n- Do nothing", offer);
        }
        finally
        {
            ScriptedLlm.Nouls = null;
            ScriptedLlm.Text = null;
            ScriptedLlm.ChooseOptionContaining = null;
        }
    }

    [Fact]
    public async Task LifeTurn_SomethingPressingOnTheMenu_IsNoTimeToTakeStock_AndGripIsNotAsked()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var start = DateTimeOffset.UtcNow.AddHours(-3).AddMinutes(-10);
        WorldActivity Did(int minutes, string app, string action, string summary)
            => new() { PersonaId = denise, PersonaName = "Denise", App = app, Action = action, Summary = summary, Ts = start.AddMinutes(minutes).ToUnixTimeMilliseconds() };
        foreach (var a in new[]
        {
            Did(0, "board", "post", "posted about the lighthouse"),
            Did(30, "board", "reply", "replied about the lighthouse"),
            Did(60, "board", "post", "posted about the lighthouse again"),
        })
            await clock.RecordAsync(a);
        var sparky = (await clock.ClaimPlayerAsync("user_a", "Sparky"))!.Value;
        await _fx.GrainFactory.GetGrain<IBoardGrain>(partyId).CreateThreadAsync(sparky, "Sparky", "Denise, do you know when the ferry leaves on Sundays?");
        var asked = new System.Collections.Concurrent.ConcurrentBag<string>();
        ScriptedLlm.ChooseOptionContaining = "Reply to";
        ScriptedLlm.Nouls = key =>
        {
            asked.Add(key);
            return key == Grip.Key ? 1 : null;
        };
        ScriptedLlm.Text = (system, user) => system.Contains("You've stopped for a moment") ? "So: I'll stop posting about the lighthouse." : null;
        try
        {
            var activity = await _fx.GrainFactory.GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, denise)).LiveAsync(null);

            Assert.NotEqual(TakingStock.Action, activity.Action);
            Assert.Equal("board", activity.App);
            Assert.Contains(Lack.Key, asked);
            Assert.DoesNotContain(Grip.Key, asked);
            Assert.DoesNotContain(ScriptedLlm.Prompts, p => p.Contains("# What you've been doing, oldest first") && p.Contains("lighthouse"));
            Assert.Equal(0, (await _fx.GrainFactory.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(partyId, denise)).GetStateAsync()).StockAt);
        }
        finally
        {
            ScriptedLlm.Nouls = null;
            ScriptedLlm.Text = null;
            ScriptedLlm.ChooseOptionContaining = null;
        }
    }
}
