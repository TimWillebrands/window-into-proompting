using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>What people say they'll do becomes something they know and owe (ADR 0043).</summary>
public class PromisesTest
{
    // Saturday 21:25 in Amsterdam winter time.
    private static readonly DateTimeOffset Sat = new(2026, 11, 7, 21, 25, 0, TimeSpan.FromHours(1));
    private static readonly Guid Pieter = Guid.NewGuid();

    private static Promise Justins(string when = PromiseRules.Soon, DateTimeOffset? at = null, bool coming = false)
        => PromiseRules.Make(true, coming, "I'll write properly tomorrow.", "what was my father working on at Meridiaan?", when, at ?? Sat);

    [Fact]
    public void Soon_PressesAtOnce_AndLapsesAfterADay()
    {
        var p = Justins();
        var ms = Sat.ToUnixTimeMilliseconds();

        Assert.True(PromiseRules.Pressing(p, ms));
        Assert.False(PromiseRules.Lapsed(p, ms + (long)TimeSpan.FromHours(23).TotalMilliseconds));
        Assert.True(PromiseRules.Lapsed(p, ms + (long)TimeSpan.FromHours(24).TotalMilliseconds));
    }

    [Fact]
    public void Tomorrow_PressesFromTheirMidnight()
    {
        var p = Justins(PromiseRules.Tomorrow);
        var midnight = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(1)).ToUnixTimeMilliseconds();

        Assert.False(PromiseRules.Pressing(p, Sat.ToUnixTimeMilliseconds()));
        Assert.True(PromiseRules.Pressing(p, midnight));
        Assert.False(PromiseRules.Lapsed(p, midnight + (long)TimeSpan.FromHours(35).TotalMilliseconds));
    }

    [Fact]
    public void ASetDay_IsKnown_ButNeverPresses()
    {
        var p = PromiseRules.Make(true, true, "thursday yes. tell sienna i'm coming", "Thursday?", PromiseRules.SetDay, Sat);

        Assert.Null(p.DueFrom);
        Assert.False(PromiseRules.Pressing(p, Sat.AddDays(3).ToUnixTimeMilliseconds()));
        Assert.True(PromiseRules.Lapsed(p, Sat.AddDays(8).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void SayingItAgain_RestatesTheOpenOne_KeepingTheFirstWords()
    {
        var open = new List<Promise> { Justins() };

        var restated = PromiseRules.Merge(open, PromiseRules.Make(true, false, "Tot morgen, Pieter. Ik laat het niet liggen.", "Goed, tot morgen.", PromiseRules.Tomorrow, Sat.AddHours(11)),
            PromiseRules.Tomorrow, Sat.AddHours(11));

        Assert.True(restated);
        var p = Assert.Single(open);
        Assert.Equal(1, p.Again);
        Assert.Equal("what was my father working on at Meridiaan?", p.Asked);
        Assert.Equal("I'll write properly tomorrow.", p.Said);
    }

    [Fact]
    public void ComingOver_AndDoingSomething_AreSeparatePromises()
    {
        var open = new List<Promise> { Justins() };

        Assert.False(PromiseRules.Merge(open, Justins(coming: true), PromiseRules.Soon, Sat));
        Assert.Equal(2, open.Count);
    }

    [Fact]
    public void ALapsedOne_IsReplaced_NotRestated()
    {
        var open = new List<Promise> { Justins() };
        var later = Sat.AddDays(2);

        Assert.False(PromiseRules.Merge(open, Justins(at: later), PromiseRules.Soon, later));
        Assert.Equal(0, Assert.Single(open).Again);
    }

    [Fact]
    public void Owed_ReadsAsAFact_WithWhatTheyWereAsked()
    {
        var promise = Justins();
        promise.Again = 2;

        var line = Assert.Single(PromiseRules.Owed(new[] { new PromiseView(Pieter, promise) }, _ => "Pieter", Sat.AddHours(1))).Text;

        Assert.Contains("You told Pieter \"I'll write properly tomorrow.\"", line);
        Assert.Contains("Meridiaan", line);
        Assert.Contains("said again 2 times", line);
    }

    [Fact]
    public void Owed_LeavesOutWhatOthersOwe()
    {
        var theirs = PromiseRules.Make(false, false, "I'll send it", null, PromiseRules.Soon, Sat);

        Assert.Empty(PromiseRules.Owed(new[] { new PromiseView(Pieter, theirs) }, _ => "Pieter", Sat));
        Assert.Single(PromiseRules.Heard(new[] { theirs }, "Pieter", Sat));
    }

    [Fact]
    public void Handles_ReachThePerson_TheirChat_AndForAComeOverWhereTheyAre()
    {
        var chat = Guid.NewGuid();
        var place = Guid.NewGuid();
        var anne = Guid.NewGuid();
        var promises = new[]
        {
            new PromiseView(Pieter, Justins()),
            new PromiseView(anne, PromiseRules.Make(true, true, "Ik kom er zo aan", null, PromiseRules.Soon, Sat)),
            new PromiseView(Guid.NewGuid(), PromiseRules.Make(true, true, "thursday", null, PromiseRules.SetDay, Sat)),
        };

        var handles = PromiseRules.Handles(promises, id => id == Pieter ? chat : null, id => id == anne ? place : Guid.NewGuid(), Sat.ToUnixTimeMilliseconds());

        Assert.Contains(Pieter, handles.Keys);
        Assert.Contains(chat, handles.Keys);
        Assert.Contains(anne, handles.Keys);
        Assert.Contains(place, handles.Keys);
        Assert.Equal(4, handles.Count);
    }

    [Fact]
    public void Addressee_InARoom_IsWhoTheyWereAnswering()
    {
        var henkie = Guid.NewGuid();
        var lines = new List<ChatMessage>
        {
            new() { SenderId = henkie, Content = "kom je nu?" },
            new() { SenderId = Narrator.PersonaId, Content = "[Happening]" },
            new() { SenderId = Pieter, Content = "ik kom eraan" },
        };

        Assert.Equal(henkie, PromiseRules.Addressee(lines, Pieter));
        Assert.Null(PromiseRules.Addressee(lines.Take(1).ToList(), henkie));
    }

    [Fact]
    public void Arrived_MeansStandingInTheSamePlace()
    {
        var cafe = Guid.NewGuid();
        Assert.True(PromiseRules.Arrived(new Body { PlaceId = cafe }, new Body { PlaceId = cafe }));
        Assert.False(PromiseRules.Arrived(new Body { PlaceId = Guid.NewGuid(), DestPlaceId = cafe }, new Body { PlaceId = cafe }));
        Assert.False(PromiseRules.Arrived(null, new Body { PlaceId = cafe }));
    }

    [Fact]
    public void Questions_AskWhetherEachOpenOneWasKept()
    {
        var questions = PromiseRules.Questions("Justin", "Pieter", new[] { Justins() });

        Assert.Contains("promise", questions.Keys);
        Assert.Contains("when", questions.Keys);
        Assert.Contains("kept0", questions.Keys);
    }

    [Fact]
    public void TiesRender_SaysWhatTheyToldYou()
    {
        var text = Ties.Render(new[] { new Acquaintance("Denise", new Tie { Exchanges = 12, LastAt = Sat.ToUnixTimeMilliseconds() }, Promised: new[] { "Denise told you \"thursday yes\" 10 h ago" }) }, Sat);

        Assert.Contains("Denise told you \"thursday yes\"", text);
    }
}

/// <summary>Owing and wanting share one handle on Initiative (ADR 0026, 0043).</summary>
public class InitiativeHandleTest
{
    private static LifeContext Ctx(IReadOnlyDictionary<Guid, string>? wanted, IReadOnlyDictionary<Guid, string>? promised)
    {
        var self = new CastMember { Id = Guid.NewGuid(), Name = "Justin" };
        return new LifeContext(Guid.Empty, self, new List<CastMember> { self }, new HashSet<Guid>(), new List<ChatGroupInfo>(), new List<WorldActivity>(),
            DateTimeOffset.UtcNow, new Dictionary<Guid, AttentionLook>(), wanted, promised);
    }

    [Fact]
    public void APromise_ReadsBeforeAWant()
    {
        var pieter = Guid.NewGuid();
        var ctx = Ctx(new Dictionary<Guid, string> { [pieter] = "his respect" }, new Dictionary<Guid, string> { [pieter] = "I'll write properly tomorrow." });
        var reply = new Affordance("messenger", "Reply to Pieter", 0, pieter, "reply");

        Assert.True(ctx.Drawn(pieter));
        Assert.Equal("Reply to Pieter (you said you would: \"I'll write properly tomorrow.\")", ctx.Read(reply));
    }

    [Fact]
    public void NothingOwedOrWanted_LeavesTheLabel()
    {
        var ctx = Ctx(null, null);
        var a = new Affordance("messenger", "Reply to Pieter", 0, Guid.NewGuid(), "reply");

        Assert.False(ctx.Drawn(a.TargetId));
        Assert.Equal("Reply to Pieter", ctx.Read(a));
    }

    [Fact]
    public void APromiseDue_OutlastsSettling()
    {
        var go = new Affordance("places", "Go to the Café", 1, Guid.NewGuid(), "go");

        Assert.Equal(2, Settling.Penalty(go, TimeSpan.FromMinutes(2)));
        Assert.Equal(0, Settling.Penalty(go, TimeSpan.FromMinutes(2), promised: true));
    }

    [Fact]
    public void ASettledChat_IsNotChased_UnlessYouOweThem()
    {
        Assert.True(MessengerManners.Chase(settled: false, owed: false));
        Assert.False(MessengerManners.Chase(settled: true, owed: false));
        Assert.True(MessengerManners.Chase(settled: true, owed: true));
    }

    [Fact]
    public void BoardThread_RunsItsCourse_OnRepeatsNotOnTopic()
    {
        Assert.True(Thermostat.RunItsCourse(new ThermostatRead(0, 0, 0.7, 0, true)));
        Assert.True(Thermostat.RunItsCourse(new ThermostatRead(0, 0, 0, 0, true, Reconfirming: 0.65)));
        Assert.False(Thermostat.RunItsCourse(new ThermostatRead(0, 0.9, 0.2, 0, true)));
    }

    [Fact]
    public void Diary_RestingIsNothingToWriteAbout()
    {
        var entry = 1000L;
        var night = Enumerable.Range(1, 6).Select(i => new WorldActivity { App = "life", Action = "rest", Ts = entry + i }).ToList();

        Assert.Equal(0, DiaryApp.Lived(night, entry));
        night.Add(new WorldActivity { App = "board", Action = "post", Ts = entry + 10 });
        night.Add(new WorldActivity { App = "board", Action = PersonaLifeGrain.LookAction, Ts = entry + 11 });
        Assert.Equal(1, DiaryApp.Lived(night, entry));
    }

    [Fact]
    public void Sleep_ADayWrittenAfterMidnight_StillLeavesANight()
    {
        var (bed, wake) = Sleep.Window(Guid.Empty, firstBeat: 3 * 60, lastBeat: 4 * 60 + 30);

        Assert.Equal(Sleep.EarliestWake, wake);
        Assert.True(Sleep.IsNight(4 * 60, bed, wake), $"bed {bed} wake {wake}");
        Assert.False(Sleep.IsNight(12 * 60, bed, wake));
    }
}

/// <summary>Happenings land at real Places and matter most to whoever's near (ADR 0043).</summary>
public class HappeningsTest
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 30, 0, TimeSpan.Zero);

    private static (List<Place> Places, Place Cafe, Place Centre, Place Zuid) World()
    {
        var cafe = new Place { Id = Guid.NewGuid(), Name = "the Café", Kind = PlaceKinds.Public, DistrictId = Districts.Centre, Purpose = "where people get coffee" };
        var centre = new Place { Id = Guid.NewGuid(), Name = "the city centre", At = "in", Kind = PlaceKinds.Public, DistrictId = Districts.Centre };
        var zuid = new Place { Id = Guid.NewGuid(), Name = "the office", Kind = PlaceKinds.Public, DistrictId = "zuid" };
        return (new List<Place> { cafe, centre, zuid }, cafe, centre, zuid);
    }

    private static WorldActivity At(Guid? place, DateTimeOffset when)
        => new() { PersonaId = Narrator.PersonaId, App = "world", Action = "happening", Summary = "A gull steals a mackerel off the counter.", TargetId = place, Ts = when.ToUnixTimeMilliseconds() };

    [Fact]
    public void Relate_Here_Nearby_Away_Anywhere()
    {
        var (places, cafe, centre, zuid) = World();
        var gull = At(cafe.Id, Now.AddMinutes(-5));

        Assert.Equal(HappeningNear.Here, HappeningRules.Relate(gull, new Body { PlaceId = cafe.Id }, places, Guid.NewGuid()).Near);
        Assert.Equal(HappeningNear.Nearby, HappeningRules.Relate(gull, new Body { PlaceId = centre.Id }, places, Guid.NewGuid()).Near);
        Assert.Equal(HappeningNear.Away, HappeningRules.Relate(gull, new Body { PlaceId = zuid.Id }, places, Guid.NewGuid()).Near);
        Assert.Equal(HappeningNear.Anywhere, HappeningRules.Relate(At(null, Now), new Body { PlaceId = cafe.Id }, places, Guid.NewGuid()).Near);
    }

    [Fact]
    public void Line_SaysWhereItWasFromHere()
    {
        var (places, cafe, _, _) = World();
        var h = HappeningRules.Relate(At(cafe.Id, Now.AddMinutes(-5)), new Body { PlaceId = cafe.Id }, places, Guid.NewGuid());

        Assert.Equal("- 5 min ago, right here at the Café: A gull steals a mackerel off the counter.", HappeningRules.Line(h, Now));
    }

    [Fact]
    public void PickPlace_IsPublic_AndLeansToWherePeopleAre()
    {
        var (places, cafe, _, _) = World();
        places.Add(new Place { Id = Guid.NewGuid(), Name = "Anne's", Kind = PlaceKinds.Home });
        places.Add(new Place { Id = Guid.NewGuid(), Name = "the old mill", Kind = PlaceKinds.Public, Dormant = true });
        var bodies = Enumerable.Range(0, 5).Select(_ => new Body { ParticipantId = Guid.NewGuid(), PlaceId = cafe.Id }).ToList();
        var random = new Random(7);

        var picks = Enumerable.Range(0, 400).Select(_ => HappeningRules.PickPlace(places, bodies, random)!).ToList();

        Assert.All(picks, p => Assert.Equal(PlaceKinds.Public, p.Kind));
        Assert.DoesNotContain(picks, p => p.Dormant);
        Assert.True(picks.Count(p => p.Id == cafe.Id) > 200);
    }

    [Fact]
    public void Setting_NamesARealPlace_AndForbidsInventedStreets()
    {
        var (_, cafe, _, _) = World();
        var setting = HappeningRules.Setting(cafe, new[] { Districts.Seed() });

        Assert.StartsWith("at the Café, in the centre (where people get coffee)", setting);
        Assert.Contains("don't invent street", setting);
    }

    [Fact]
    public void Around_IsThePlaceAndItsDistrict_WitnessesAreThoseAwakeThere()
    {
        var (places, cafe, centre, zuid) = World();
        var here = new Body { ParticipantId = Guid.NewGuid(), PlaceId = cafe.Id };
        var asleep = new Body { ParticipantId = Guid.NewGuid(), PlaceId = cafe.Id, Asleep = true };
        var near = new Body { ParticipantId = Guid.NewGuid(), PlaceId = centre.Id };
        var far = new Body { ParticipantId = Guid.NewGuid(), PlaceId = zuid.Id };
        var bodies = new[] { here, asleep, near, far };

        Assert.Equal(new[] { here.ParticipantId }, HappeningRules.Witnesses(bodies, cafe.Id));
        Assert.DoesNotContain(far.ParticipantId, HappeningRules.Around(places, bodies, cafe.Id));
        Assert.Contains(near.ParticipantId, HappeningRules.Around(places, bodies, cafe.Id));
    }

    [Fact]
    public void Witnessed_IsFresh_UntilThatChannelIsUsed()
    {
        var (places, cafe, _, _) = World();
        var h = HappeningRules.Relate(At(cafe.Id, Now.AddMinutes(-5)), new Body { PlaceId = cafe.Id }, places, Guid.NewGuid());
        var stale = h with { Ts = Now.AddHours(-2).ToUnixTimeMilliseconds() };
        var said = new WorldActivity { App = "places", Action = "say", Ts = Now.AddMinutes(-1).ToUnixTimeMilliseconds() };

        Assert.Equal(h, HappeningRules.Witnessed(new[] { h }, Array.Empty<WorldActivity>(), "places", "say", Now));
        Assert.Null(HappeningRules.Witnessed(new[] { stale }, Array.Empty<WorldActivity>(), "places", "say", Now));
        Assert.Null(HappeningRules.Witnessed(new[] { h }, new[] { said }, "places", "say", Now));
        Assert.Equal(h, HappeningRules.Witnessed(new[] { h }, new[] { said }, "board", "post", Now));
    }

    [Fact]
    public void Hooks_CarryTheHappening()
    {
        Assert.Equal("Say something to Anne about what just happened here (a gull)", Hooks.Saw("Anne", "a gull"));
        Assert.StartsWith("Post on Partyboard about what just happened at the Café", Hooks.PostAbout("at the Café", "a gull"));
    }
}
