using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Desires;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>What people say they'll do is a Desire of theirs, and a set-day meet-up a Gathering (ADR 0043).</summary>
public class PromisesTest
{
    // Saturday 21:25 in Amsterdam winter time.
    private static readonly DateTimeOffset Sat = new(2026, 11, 7, 21, 25, 0, TimeSpan.FromHours(1));
    private static readonly Guid Pieter = Guid.NewGuid();

    private static Desire Justins(string when = PromiseRules.Soon, DateTimeOffset? at = null, bool coming = false)
        => PromiseRules.Make(Pieter, coming, "I'll write properly tomorrow.", "what was my father working on at Meridiaan?", when, at ?? Sat);

    [Fact]
    public void APromise_IsTheirDesireToKeepTheirWord()
    {
        var d = Justins();

        Assert.Equal(DesireSource.Promise, d.Source);
        Assert.Equal(DesireKind.Being, d.Kind);
        Assert.Equal(Pieter, d.TargetId);
        Assert.Equal("\"I'll write properly tomorrow.\", answering \"what was my father working on at Meridiaan?\"", d.Text);
    }

    [Fact]
    public void Soon_PressesAtOnce_AndLapsesAfterADay()
    {
        var d = Justins();
        var ms = Sat.ToUnixTimeMilliseconds();

        Assert.True(DesireRules.Pressing(d, ms));
        Assert.False(DesireRules.Fades(d, intrinsic: false, Sat.AddHours(23)));
        Assert.True(DesireRules.Fades(d, intrinsic: false, Sat.AddHours(24)));
    }

    [Fact]
    public void Tomorrow_PressesFromTheirMidnight()
    {
        var d = Justins(PromiseRules.Tomorrow);
        var midnight = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(1));

        Assert.False(DesireRules.Pressing(d, Sat.ToUnixTimeMilliseconds()));
        Assert.True(DesireRules.Pressing(d, midnight.ToUnixTimeMilliseconds()));
        Assert.False(DesireRules.Fades(d, intrinsic: false, midnight.AddHours(35)));
    }

    [Fact]
    public void ASetDay_IsKnown_ButNeverPresses()
    {
        var d = PromiseRules.Make(Pieter, true, "thursday yes. tell sienna i'm coming", "Thursday?", PromiseRules.SetDay, Sat);

        Assert.Null(d.DueAt);
        Assert.False(DesireRules.Pressing(d, Sat.AddDays(3).ToUnixTimeMilliseconds()));
        Assert.True(DesireRules.Fades(d, intrinsic: false, Sat.AddDays(8)));
    }

    [Fact]
    public void ALapse_LetsDownOnlyWhatWasDue()
    {
        var lapsedSoon = Justins();
        var lapsedSetDay = PromiseRules.Make(Pieter, true, "thursday", null, PromiseRules.SetDay, Sat);
        var later = Sat.AddDays(9).ToUnixTimeMilliseconds();

        Assert.True(PromiseRules.LetDown(lapsedSoon, later));
        Assert.False(PromiseRules.LetDown(lapsedSetDay, later));
        Assert.False(PromiseRules.LetDown(Justins(), Sat.ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void SayingItAgain_RestatesTheOpenOne_KeepingTheFirstWords()
    {
        var open = new List<Desire> { Justins() };

        var restated = PromiseRules.Restate(open, PromiseRules.Make(Pieter, false, "Tot morgen, Pieter. Ik laat het niet liggen.", "Goed, tot morgen.", PromiseRules.Tomorrow, Sat.AddHours(11)),
            Sat.AddHours(11));

        Assert.True(restated);
        var d = Assert.Single(open);
        Assert.Equal(1, d.Again);
        Assert.Contains("I'll write properly tomorrow.", d.Text);
        Assert.Contains("Meridiaan", d.Text);
    }

    [Fact]
    public void ComingOver_AndDoingSomething_AreSeparatePromises()
    {
        Assert.False(PromiseRules.Restate(new List<Desire> { Justins() }, Justins(coming: true), Sat));
    }

    [Fact]
    public void ALapsedOrSettledOne_IsNotRestated()
    {
        var lapsed = new List<Desire> { Justins() };
        var kept = Justins();
        kept.Status = DesireStatus.Fulfilled;

        Assert.False(PromiseRules.Restate(lapsed, Justins(at: Sat.AddDays(2)), Sat.AddDays(2)));
        Assert.False(PromiseRules.Restate(new List<Desire> { kept }, Justins(), Sat));
    }

    [Fact]
    public void Promises_AreOwed_NotWanted_SoTheyStayOutOfWhatYouWant()
    {
        var want = new Desire { Text = "get your sourdough right", Kind = DesireKind.Being, Since = Sat.ToUnixTimeMilliseconds() };
        var desires = new[] { want, Justins() };

        Assert.Equal(new[] { want }, DesireRules.OnMind(desires));
        Assert.DoesNotContain("write properly", DesireRules.Render(desires));
        Assert.Single(PromiseRules.Owed(desires, _ => "Pieter", Sat.AddHours(1)));
    }

    [Fact]
    public void Owed_ReadsAsAFact_WithWhatTheyWereAsked()
    {
        var d = Justins();
        d.Again = 2;

        var line = Assert.Single(PromiseRules.Owed(new[] { d }, _ => "Pieter", Sat.AddHours(1))).Text;

        Assert.Contains("You told Pieter \"I'll write properly tomorrow.\"", line);
        Assert.Contains("Meridiaan", line);
        Assert.Contains("said again 2 times", line);
    }

    [Fact]
    public void Owed_LeavesOutWhatsSettledOrLapsed()
    {
        var kept = Justins();
        kept.Status = DesireStatus.Fulfilled;

        Assert.Empty(PromiseRules.Owed(new[] { kept }, _ => "Pieter", Sat));
        Assert.Empty(PromiseRules.Owed(new[] { Justins() }, _ => "Pieter", Sat.AddDays(2)));
    }

    [Fact]
    public void Heard_IsReadOffThePromisersOwnDesires_ForThatHolderOnly()
    {
        var toSomeoneElse = PromiseRules.Make(Guid.NewGuid(), false, "I'll send it", null, PromiseRules.Soon, Sat);

        var line = Assert.Single(PromiseRules.Heard(new[] { Justins(), toSomeoneElse }, Pieter, "Justin", Sat.AddHours(10)));

        Assert.StartsWith("Justin told you \"I'll write properly tomorrow.\"", line);
    }

    [Fact]
    public void ADuePromise_IsAlwaysInTheHandles_PastTheTwoWantsOnTheMind()
    {
        var chat = Guid.NewGuid();
        var place = Guid.NewGuid();
        var anne = Guid.NewGuid();
        var wants = Enumerable.Range(0, 3).Select(i => new Desire
        {
            Text = $"want {i}", Kind = DesireKind.Being, TargetId = Guid.NewGuid(), Since = Sat.AddMinutes(i).ToUnixTimeMilliseconds(),
        });
        var desires = wants.Concat(new[]
        {
            Justins(),
            PromiseRules.Make(anne, true, "Ik kom er zo aan", null, PromiseRules.Soon, Sat),
            PromiseRules.Make(Guid.NewGuid(), true, "thursday", null, PromiseRules.SetDay, Sat),
        }).ToList();

        var handles = DesireRules.People(desires, Array.Empty<(Guid, string)>(), Sat.ToUnixTimeMilliseconds(),
            id => id == Pieter ? chat : null, id => id == anne ? place : Guid.NewGuid());

        Assert.Equal(DesireSource.Promise, handles[Pieter].Source);
        Assert.Equal(DesireSource.Promise, handles[chat].Source);
        Assert.Equal(DesireSource.Promise, handles[anne].Source);
        Assert.Equal(DesireSource.Promise, handles[place].Source);
        // Four promise keys plus the two wants on the mind; the set-day one doesn't press.
        Assert.Equal(6, handles.Count);
    }

    [Fact]
    public void APromise_ReadsBeforeAWant_ForTheSamePerson()
    {
        var want = new Desire { Text = "his respect", Kind = DesireKind.Being, TargetId = Pieter, Since = Sat.ToUnixTimeMilliseconds() };

        var handles = DesireRules.People(new[] { want, Justins() }, Array.Empty<(Guid, string)>(), Sat.ToUnixTimeMilliseconds());

        Assert.StartsWith("Reply to Pieter (you said you would: \"I'll write properly tomorrow.\"", DesireRules.Toward("Reply to Pieter", handles[Pieter]));
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
    public void Questions_AskKeptOrOffForEachOpenOne_AndTheMeetupsDayAndHome()
    {
        var questions = PromiseRules.Questions("Justin", "Pieter", new[] { Justins() }, Sat);

        Assert.Contains("promise", questions.Keys);
        Assert.Contains("kept0", questions.Keys);
        Assert.Contains("off0", questions.Keys);
        var day = Assert.IsType<ChoiceQuestion>(questions["day"]);
        Assert.Equal(7, day.Options.Count);
        Assert.Equal("Thursday 12 November", day.Options["2026-11-12"]);
        Assert.Contains("where", questions.Keys);
    }

    [Fact]
    public void Meetup_NeedsASureDayAndAHome_PartOfDayDefaultsToTheEvening()
    {
        var thursday = ("2026-11-12", 0.8);
        var home = (PromiseRules.OtherHome, 0.7);

        var plan = PromiseRules.Meetup(thursday, (PromiseRules.Morning, 0.3), home, Sat);

        Assert.NotNull(plan);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 19, 0, 0, TimeSpan.FromHours(1)).ToUnixTimeMilliseconds(), plan.Value.StartsAt);
        Assert.Equal(PromiseRules.OtherHome, plan.Value.Where);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(1)).ToUnixTimeMilliseconds(),
            PromiseRules.Meetup(thursday, (PromiseRules.Morning, 0.9), home, Sat)!.Value.StartsAt);
        Assert.Null(PromiseRules.Meetup(("2026-11-12", 0.4), (PromiseRules.Evening, 1), home, Sat));
        Assert.Null(PromiseRules.Meetup(thursday, (PromiseRules.Evening, 1), (PromiseRules.Elsewhere, 0.9), Sat));
        Assert.Null(PromiseRules.Meetup(thursday, (PromiseRules.Evening, 1), (PromiseRules.TheirHome, 0.4), Sat));
    }

    [Fact]
    public void Already_FindsThePlannedGatheringBothGoTo_WithinADay()
    {
        var (anne, denise) = (Guid.NewGuid(), Guid.NewGuid());
        var at = Sat.AddDays(5).ToUnixTimeMilliseconds();
        var supper = new Gathering { HostId = anne, AttendeeIds = new List<Guid> { anne, denise }, StartsAt = at, Status = GatheringStatus.Planned };

        Assert.Same(supper, PromiseRules.Already(new[] { supper }, denise, anne, at + (long)TimeSpan.FromHours(20).TotalMilliseconds));
        Assert.Null(PromiseRules.Already(new[] { supper }, denise, anne, at + (long)TimeSpan.FromDays(2).TotalMilliseconds));
        Assert.Null(PromiseRules.Already(new[] { supper }, denise, Guid.NewGuid(), at));
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
    private static LifeContext Ctx(IReadOnlyDictionary<Guid, Desire>? wanted)
    {
        var self = new CastMember { Id = Guid.NewGuid(), Name = "Justin" };
        return new LifeContext(Guid.Empty, self, new List<CastMember> { self }, new HashSet<Guid>(), new List<ChatGroupInfo>(), new List<WorldActivity>(),
            DateTimeOffset.UtcNow, new Dictionary<Guid, AttentionLook>(), wanted);
    }

    [Fact]
    public void Owes_IsAPromiseInTheHandle_NotAWant()
    {
        var (pieter, anne) = (Guid.NewGuid(), Guid.NewGuid());
        var ctx = Ctx(new Dictionary<Guid, Desire>
        {
            [pieter] = PromiseRules.Make(pieter, false, "I'll write properly tomorrow.", null, PromiseRules.Soon, DateTimeOffset.UtcNow),
            [anne] = new Desire { Text = "her approval", Kind = DesireKind.Being, TargetId = anne },
        });

        Assert.True(ctx.Owes(pieter));
        Assert.False(ctx.Owes(anne));
        Assert.NotNull(ctx.WantAbout(anne));
    }

    [Fact]
    public void NothingOwedOrWanted_LeavesTheLabel()
    {
        var ctx = Ctx(null);
        var a = new Affordance("messenger", "Reply to Pieter", 0, Guid.NewGuid(), "reply");

        Assert.Null(ctx.WantAbout(a.TargetId));
        Assert.False(ctx.Owes(a.TargetId));
        Assert.Equal("Reply to Pieter", DesireRules.Toward(a.Label, ctx.WantAbout(a.TargetId)));
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
