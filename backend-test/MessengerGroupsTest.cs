using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Storage;
using Orleans.TestingHost;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Auth;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Relations;
using PartyTown.World.Self;

namespace BackendTest;

public partial class WorldLifeTest
{
    private async Task<(Guid PartyId, Guid Player, Guid Denise, Guid Pieter, Guid Anne)> GroupWorldAsync()
    {
        var (partyId, player, denise, _) = await PlayerWorldAsync();
        var pieter = await AddPersonaAsync(partyId, "Pieter");
        var anne = await AddPersonaAsync(partyId, "Anne");
        return (partyId, player, denise, pieter, anne);
    }

    private async Task<Guid> AddPersonaAsync(Guid partyId, string name)
    {
        var id = Guid.NewGuid();
        await _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(new Persona(id, name, $"You are {name}.", "Someone."));
        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).AddParticipantsAsync(new List<PartyParticipant> { new() { Id = id, Name = name, Driver = DriverKind.LLM } });
        return id;
    }

    private Task Know(Guid partyId, Guid a, Guid b)
        => _fx.GrainFactory.GetGrain<IRelationsGrain>(partyId).DeclareAsync(a, b, RelationKind.Friends, null, past: true);

    private async Task<(AppPerception Seen, LifeContext Ctx)> GroupsSeenByAsync(Guid partyId, Guid who, Guid player)
    {
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var cast = await party.GetCastAsync();
        var ctx = new LifeContext(partyId, cast.First(c => c.Id == who), cast, new HashSet<Guid> { player }, await party.GetChatGroups(),
            new List<WorldActivity>(), DateTimeOffset.UtcNow, new Dictionary<Guid, AttentionLook>());
        return ((await new MessengerApp(_fx.GrainFactory).PerceiveAsync(ctx, CancellationToken.None))!, ctx);
    }

    private static async Task SettledAsync(IChatGroupGrain room, params Guid[] personas)
    {
        await Task.Delay(200);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            var busy = false;
            foreach (var p in personas) busy |= await room.IsAnsweringAsync(p);
            if (!busy) return;
            await Task.Delay(50);
        }
    }

    private async Task<HashSet<Guid>> MembersOf(Guid partyId, Guid groupId)
    {
        var listed = (await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetChatGroups()).Single(g => g.Id == groupId).MemberIds!.ToHashSet();
        Assert.True(listed.SetEquals(await _fx.GrainFactory.GetGrain<IChatGroupGrain>(groupId).GetParticipantIdsAsync()));
        return listed;
    }

    [Fact]
    public async Task Messenger_StartsAGroup_OnlyWithTwoPeopleTheyKnow()
    {
        var (partyId, player, denise, pieter, anne) = await GroupWorldAsync();
        await Know(partyId, denise, pieter);

        var (alone, _) = await GroupsSeenByAsync(partyId, denise, player);
        Assert.DoesNotContain(alone.Affordances, a => a.Verb == "start");

        await Know(partyId, denise, anne);
        var (seen, _) = await GroupsSeenByAsync(partyId, denise, player);

        var start = Assert.Single(seen.Affordances, a => a.Verb == "start");
        Assert.Equal(new[] { pieter, anne }.OrderBy(id => id), start.With!.OrderBy(id => id));
        Assert.StartsWith("Start a group chat with", start.Label);
        Assert.Equal(0, start.Priority);
    }

    [Fact]
    public async Task Messenger_StartsAtMostOneGroupADay()
    {
        var (partyId, player, denise, pieter, anne) = await GroupWorldAsync();
        var bram = await AddPersonaAsync(partyId, "Bram");
        foreach (var other in new[] { pieter, anne, bram }) await Know(partyId, denise, other);
        await Conversations.StartAsync(_fx.GrainFactory, partyId, denise, new[] { pieter, anne });

        var (seen, _) = await GroupsSeenByAsync(partyId, denise, player);

        Assert.DoesNotContain(seen.Affordances, a => a.Verb == "start");
    }

    [Fact]
    public async Task Messenger_StartsNoGroup_TheyAlreadyShare()
    {
        var (partyId, player, denise, pieter, anne) = await GroupWorldAsync();
        await Know(partyId, denise, pieter);
        await Know(partyId, denise, anne);
        await Conversations.StartAsync(_fx.GrainFactory, partyId, player, new[] { denise, pieter, anne });

        var (seen, _) = await GroupsSeenByAsync(partyId, denise, player);

        Assert.DoesNotContain(seen.Affordances, a => a.Verb == "start");
    }

    [Fact]
    public void GroupManners_CountOnlyTodaysOwnGroups()
    {
        var me = Guid.NewGuid();
        var now = DateTimeOffset.Now;
        ChatGroupInfo Made(Guid by, DateTimeOffset at, string kind = ChatGroupKinds.Group) => new() { Kind = kind, CreatedBy = by, CreatedAt = at.ToUnixTimeMilliseconds() };

        Assert.True(GroupManners.StartedToday(new[] { Made(me, now) }, me, now));
        Assert.False(GroupManners.StartedToday(new[] { Made(me, now.AddDays(-1)), Made(Guid.NewGuid(), now), Made(me, now, ChatGroupKinds.Direct) }, me, now));
        var people = new[] { me, Guid.NewGuid() };
        Assert.True(GroupManners.Share(new[] { ((IReadOnlyCollection<Guid>)people.Append(Guid.NewGuid()).ToList(), now.ToUnixTimeMilliseconds()) }, people, now));
        Assert.False(GroupManners.Share(new[] { ((IReadOnlyCollection<Guid>)people.ToList(), now.AddDays(-3).ToUnixTimeMilliseconds()) }, people, now));
    }

    [Fact]
    public async Task Messenger_InvitesSomeoneTheyKnow_OnlyIfTheyKnowAMember()
    {
        var (partyId, player, denise, pieter, anne) = await GroupWorldAsync();
        var group = await Conversations.StartAsync(_fx.GrainFactory, partyId, player, new[] { denise, pieter });
        await Know(partyId, denise, anne);

        var (before, _) = await GroupsSeenByAsync(partyId, denise, player);
        Assert.DoesNotContain(before.Affordances, a => a.Verb == "invite");

        await Know(partyId, anne, pieter);
        var (seen, ctx) = await GroupsSeenByAsync(partyId, denise, player);

        var invite = Assert.Single(seen.Affordances, a => a.Verb == "invite");
        Assert.Equal(group.Id, invite.TargetId);
        Assert.Equal(new[] { anne }, invite.With);
        Assert.Contains("Anne knows Pieter", invite.Label);

        var outcome = await new MessengerApp(_fx.GrainFactory).ActAsync(ctx, invite, "bring Anne in", null, CancellationToken.None);

        Assert.Equal("invite", outcome!.Action);
        Assert.Equal(anne, outcome.WithPersonaId);
        Assert.Contains(anne, await MembersOf(partyId, group.Id));
        Assert.Contains(player, outcome.Among!);
    }

    [Fact]
    public async Task Messenger_TalksInGroupsByTheirMembers_AndLeavesOnesThatRanTheirCourse()
    {
        var (partyId, player, denise, pieter, anne) = await GroupWorldAsync();
        var group = await Conversations.StartAsync(_fx.GrainFactory, partyId, player, new[] { denise, pieter, anne }, "kitchen");
        var room = _fx.GrainFactory.GetGrain<IChatGroupGrain>(group.Id);
        for (var i = 0; i < 4; i++)
            await room.SendNewMessageAsync(i % 2 == 0 ? anne : pieter, $"line {i}");
        await SettledAsync(room, denise, pieter, anne);

        var (fresh, _) = await GroupsSeenByAsync(partyId, denise, player);
        var say = Assert.Single(fresh.Affordances, a => a.Verb == "say");
        Assert.StartsWith("Say something in the group chat with", say.Label);
        Assert.Contains("Pieter", say.Label);
        Assert.Contains("Anne", say.Label);
        Assert.DoesNotContain("#", fresh.Block);
        Assert.DoesNotContain(fresh.Affordances, a => a.Verb == "leave");

        ScriptedLlm.Nouls = key => key == "fizzling" ? 0.9 : 0;
        try
        {
            await room.SendNewMessageAsync(anne, "ok");
            await SettledAsync(room, denise, pieter, anne);
            var (stale, ctx) = await GroupsSeenByAsync(partyId, denise, player);
            var leave = Assert.Single(stale.Affordances, a => a.Verb == "leave");
            Assert.Equal(group.Id, leave.TargetId);

            var outcome = await new MessengerApp(_fx.GrainFactory).ActAsync(ctx, leave, "leave it", null, CancellationToken.None);

            Assert.Equal("leave", outcome!.Action);
            Assert.DoesNotContain(denise, await MembersOf(partyId, group.Id));
            Assert.Contains(denise, outcome.Among!);
        }
        finally
        {
            ScriptedLlm.Nouls = null;
        }
    }

    [Fact]
    public async Task StartingAGroup_NeedsTwoOthersFromThisWorld_AndIsNamedForItsMembers()
    {
        var (partyId, player, denise, pieter, _) = await GroupWorldAsync();

        await Assert.ThrowsAsync<ConversationRefused>(() => Conversations.StartAsync(_fx.GrainFactory, partyId, player, new[] { denise }));
        await Assert.ThrowsAsync<ConversationRefused>(() => Conversations.StartAsync(_fx.GrainFactory, partyId, player, new[] { denise, Guid.NewGuid() }));
        var group = await Conversations.StartAsync(_fx.GrainFactory, partyId, player, new[] { denise, pieter });

        Assert.Equal("Sparky, Denise and Pieter", group.Name);
        Assert.Equal(ChatGroupKinds.Group, group.Kind);
        Assert.Equal(player, group.CreatedBy);
        Assert.True(new[] { player, denise, pieter }.ToHashSet().SetEquals(await MembersOf(partyId, group.Id)));
    }

    [Fact]
    public async Task InvitingFromADm_StartsANewGroup_AndLeavesTheDmAlone()
    {
        var (partyId, player, denise, pieter, _) = await GroupWorldAsync();
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var dm = await party.GetOrCreateDirectChatAsync(player, denise);
        await _fx.GrainFactory.GetGrain<IChatGroupGrain>(dm.Id).SendNewMessageAsync(player, "just between us");

        var (group, started) = await Conversations.InviteAsync(_fx.GrainFactory, partyId, player, dm.Id, pieter);

        Assert.True(started);
        Assert.NotEqual(dm.Id, group.Id);
        Assert.Equal(ChatGroupKinds.Group, group.Kind);
        Assert.True(new[] { player, denise, pieter }.ToHashSet().SetEquals(await MembersOf(partyId, group.Id)));
        Assert.Empty(await _fx.GrainFactory.GetGrain<IChatGroupGrain>(group.Id).GetMessagesAsync());
        var dmAfter = (await party.GetChatGroups()).Single(g => g.Id == dm.Id);
        Assert.Equal(ChatGroupKinds.Direct, dmAfter.Kind);
        Assert.True(new[] { player, denise }.ToHashSet().SetEquals(dmAfter.MemberIds!));
        Assert.True(new[] { player, denise }.ToHashSet().SetEquals(await _fx.GrainFactory.GetGrain<IChatGroupGrain>(dm.Id).GetParticipantIdsAsync()));
        Assert.Equal("just between us", Assert.Single(await _fx.GrainFactory.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(10)).Content);
    }

    [Fact]
    public async Task InvitingIntoAGroup_AddsThem_AndTheyCanReadWhatWasSaid()
    {
        var (partyId, player, denise, pieter, anne) = await GroupWorldAsync();
        var group = await Conversations.StartAsync(_fx.GrainFactory, partyId, player, new[] { denise, pieter });
        await _fx.GrainFactory.GetGrain<IChatGroupGrain>(group.Id).SendNewMessageAsync(player, "welcome, all");

        await Assert.ThrowsAsync<ConversationRefused>(() => Conversations.InviteAsync(_fx.GrainFactory, partyId, anne, group.Id, anne));
        var (same, started) = await Conversations.InviteAsync(_fx.GrainFactory, partyId, denise, group.Id, anne);

        Assert.False(started);
        Assert.Equal(group.Id, same.Id);
        Assert.Contains(anne, await MembersOf(partyId, group.Id));
        Assert.Contains(await _fx.GrainFactory.GetGrain<IChatGroupGrain>(group.Id).GetMessagesUntilAsync(int.MaxValue, anne), m => m.Content == "welcome, all");
        await Assert.ThrowsAsync<ConversationRefused>(() => Conversations.InviteAsync(_fx.GrainFactory, partyId, denise, group.Id, anne));
    }

    [Fact]
    public async Task LeavingAGroup_TakesThemOut_AndTheyCanNoLongerSpeakThere()
    {
        var (partyId, player, denise, pieter, _) = await GroupWorldAsync();
        var group = await Conversations.StartAsync(_fx.GrainFactory, partyId, player, new[] { denise, pieter });
        var dm = await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetOrCreateDirectChatAsync(player, denise);
        var actors = new Actors(_fx.GrainFactory);
        Assert.Equal(player, await actors.SpeakAsync(Agent(), group.Id, null));

        await Conversations.LeaveAsync(_fx.GrainFactory, partyId, player, group.Id);

        Assert.DoesNotContain(player, await MembersOf(partyId, group.Id));
        await Assert.ThrowsAsync<ForbiddenActorException>(() => actors.SpeakAsync(Agent(), group.Id, null));
        await Assert.ThrowsAsync<ConversationRefused>(() => Conversations.LeaveAsync(_fx.GrainFactory, partyId, player, group.Id));
        await Assert.ThrowsAsync<ConversationRefused>(() => Conversations.LeaveAsync(_fx.GrainFactory, partyId, player, dm.Id));
    }

    [Fact]
    public async Task ALegacyRoomWithAHuman_ConfidesOnlyOnceTheHumanWroteThere()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync(withRoom: true);
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var player = (await clock.ClaimPlayerAsync("user_a", "Sparky"))!.Value;
        var everyone = await party.CreateChatGroup("lounge");
        Assert.Contains(player, everyone.MemberIds!);
        Assert.Contains(denise, everyone.MemberIds!);
        await _fx.GrainFactory.GetGrain<IChatGroupGrain>(everyone.Id).SendNewMessageAsync(denise, "anyone around?");

        async Task<List<Guid>> FreeAsync() => (await Takeover.FreeAsync(_fx.GrainFactory, (await party.GetParty()).Participants,
            await clock.GetClaimedAsync(), await party.GetChatGroups())).Select(p => p.Id).ToList();

        Assert.Contains(denise, await FreeAsync());

        await _fx.GrainFactory.GetGrain<IChatGroupGrain>(everyone.Id).SendNewMessageAsync(player, "me!");

        Assert.DoesNotContain(denise, await FreeAsync());
        Assert.False(await clock.TakePersonaAsync("user_b", denise));
    }

    [Fact]
    public async Task Calendar_OffersPlanningOnlyAtPlaces()
    {
        var (partyId, player, denise, pieter, _) = await GroupWorldAsync();
        await Conversations.StartAsync(_fx.GrainFactory, partyId, player, new[] { denise, pieter });
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var cast = await party.GetCastAsync();
        var ctx = new LifeContext(partyId, cast.First(c => c.Id == denise), cast, new HashSet<Guid> { player }, await party.GetChatGroups(),
            new List<WorldActivity>(), DateTimeOffset.UtcNow, new Dictionary<Guid, AttentionLook>());

        var seen = await new CalendarApp(_fx.GrainFactory, new ScriptedLlm()).PerceiveAsync(ctx, CancellationToken.None);

        Assert.DoesNotContain(seen!.Affordances, a => a.Verb == "plan");
        Assert.Null(await new CalendarApp(_fx.GrainFactory, new ScriptedLlm()).ActAsync(ctx, new Affordance("calendar", "Plan", 0, (await party.GetChatGroups()).First().Id, "plan"), "x", null, CancellationToken.None));
    }

    [Fact]
    public async Task UpcomingGatheringsWithoutAPlace_AreCancelled_WhenTheCalendarWakes()
    {
        var partyId = Guid.NewGuid();
        var calendar = _fx.GrainFactory.GetGrain<ICalendarGrain>(partyId);
        var soon = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var legacy = new CalendarState
        {
            Gatherings = new List<Gathering>
            {
                new() { Id = Guid.NewGuid(), Title = "In the kitchen", RoomId = Guid.NewGuid(), RoomName = "kitchen", StartsAt = soon },
                new() { Id = Guid.NewGuid(), Title = "Coffee", RoomName = "the Café", PlaceId = Guid.NewGuid(), StartsAt = soon },
                new() { Id = Guid.NewGuid(), Title = "Long done", RoomId = Guid.NewGuid(), RoomName = "kitchen", StartsAt = soon - 86_400_000, Status = GatheringStatus.Over },
            },
        };
        var storage = ((InProcessSiloHandle)_fx.Cluster.Primary).SiloHost.Services.GetRequiredKeyedService<IGrainStorage>("parties");
        await storage.WriteStateAsync("calendar", calendar.GetGrainId(), new GrainState<CalendarState>(legacy));

        var gatherings = await calendar.GetGatheringsAsync();

        var cancelled = Assert.Single(gatherings, g => g.Title == "In the kitchen");
        Assert.Equal(GatheringStatus.Cancelled, cancelled.Status);
        Assert.Equal(CalendarGrain.NoVenue, cancelled.CancelledBecause);
        Assert.Equal(GatheringStatus.Planned, gatherings.Single(g => g.Title == "Coffee").Status);
        Assert.Equal(GatheringStatus.Over, gatherings.Single(g => g.Title == "Long done").Status);
        Assert.Equal("Coffee", Assert.Single(await calendar.StartDueAsync(soon + 1)).Title);
    }

    [Fact]
    public void LegacyPartyEvents_ReplayAsNoOps_AndMembersAreKept()
    {
        var group = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var history = new List<PartyEvent>
        {
            new PartySetEvent { PartyId = Guid.NewGuid(), Name = "p" },
            new ChatGroupCreatedEvent { ChatGroup = new ChatGroupInfo { Id = group, Name = "kitchen", Scenario = "A bar." } },
            new ChatGroupScenarioUpdatedEvent { ChatGroupId = group, Scenario = "A rooftop." },
            new ChatGroupMembersSetEvent { ChatGroupId = group, MemberIds = new List<Guid> { alice } },
        };

        var state = new PartyState();
        foreach (var e in history) ((dynamic)state).Apply((dynamic)e);

        var kitchen = Assert.Single(state.ChatGroups);
        Assert.Null(kitchen.Scenario);
        Assert.Equal(ChatGroupKinds.Group, kitchen.Kind);
        Assert.Equal(new[] { alice }, kitchen.MemberIds);
    }

    [Fact]
    public void LegacyEvents_StillDeserialise()
    {
        var serializer = _fx.Cluster.ServiceProvider.GetRequiredService<Serializer>();
        var persona = Guid.NewGuid();
        var events = new ChatGroupEvent[]
        {
            new ChatGroupScenarioSetEvent { Scenario = "A bar." },
            new ChatGroupDriverOverrideSetEvent { PersonaId = persona, Kind = DriverKind.User },
            new ChatGroupDriverOverrideClearedEvent { PersonaId = persona },
            new ChatGroupDriverOverridesReplacedEvent { Overrides = new Dictionary<Guid, DriverKind> { [persona] = DriverKind.LLM } },
        };

        foreach (var e in events)
            Assert.Equal(e.GetType(), serializer.Deserialize<ChatGroupEvent>(serializer.SerializeToArray(e)).GetType());
        var party = new ChatGroupScenarioUpdatedEvent { ChatGroupId = persona, Scenario = "x" };
        Assert.Equal(persona, ((ChatGroupScenarioUpdatedEvent)serializer.Deserialize<PartyEvent>(serializer.SerializeToArray<PartyEvent>(party))).ChatGroupId);
    }
}
