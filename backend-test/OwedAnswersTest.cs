using System.Collections.Concurrent;
using BackendTest.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orleans.Hosting;
using Orleans.TestingHost;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.World;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>A human is never left on read (ADR 0031, #129): the bookkeeping alone, no cluster.</summary>
public class OwedAnswersTest
{
    private static readonly Guid Human = Guid.NewGuid();
    private static readonly Guid Anne = Guid.NewGuid();
    private static readonly Guid Bas = Guid.NewGuid();

    private static SelfView Self(Guid id, double chattiness = 0.5) => new(id, "x", DriverKind.LLM, null, null, chattiness, 0.5);

    private static readonly SelfView[] Both = { Self(Anne), Self(Bas) };

    private static OwedAnswers Owed(params Guid[] listeners)
    {
        var owed = new OwedAnswers(TimeSpan.FromMinutes(10));
        owed.Expect(1, Human, listeners, 0);
        return owed;
    }

    [Fact]
    public void EveryoneLetItPass_TheKeenestAnswers_Once()
    {
        var owed = Owed(Anne, Bas);
        owed.Urge(1, Anne, 0.5);
        owed.Ended(1, Anne);
        Assert.Empty(owed.Due(_ => false, Both));
        owed.Urge(1, Bas, 0.8);
        owed.Ended(1, Bas);

        Assert.Equal((1, Bas), Assert.Single(owed.Due(_ => false, Both)));
        Assert.Empty(owed.Due(_ => false, Both));
        Assert.False(owed.Owes(1));
    }

    [Fact]
    public void SameUrge_TheChattierOneSpeaksUp()
    {
        var owed = Owed(Anne, Bas);
        owed.Ended(1, Anne);
        owed.Ended(1, Bas);

        Assert.Equal(Anne, Assert.Single(owed.Due(_ => false, new[] { Self(Anne, 0.9), Self(Bas, 0.2) })).Who);
    }

    [Fact]
    public void ALineTheyHeard_AnswersIt()
    {
        var owed = Owed(Anne, Bas);
        owed.Landed(2, Anne, null);

        Assert.False(owed.Owes(1));
    }

    [Fact]
    public void ALineSaidOutOfTheirEarshot_DoesNot()
    {
        var owed = Owed(Anne, Bas);
        owed.Landed(2, Anne, new List<Guid> { Anne, Bas });

        Assert.True(owed.Owes(1));
    }

    [Fact]
    public void TheirOwnNextLine_TakesOverTheOlderOne()
    {
        var owed = Owed(Anne);
        owed.Landed(2, Human, new List<Guid> { Human });

        Assert.False(owed.Owes(1));
    }

    [Fact]
    public void AFailedTurn_IsNotADecline()
    {
        var owed = Owed(Anne, Bas);
        owed.Ended(1, Anne);
        owed.Ended(1, Bas, failed: true);

        Assert.Empty(owed.Due(_ => false, Both));
        Assert.False(owed.Owes(1));
    }

    [Fact]
    public void ATurnStillOpen_IsWaitedFor()
    {
        var owed = Owed(Anne);
        owed.Ended(1, Anne);

        Assert.Empty(owed.Due(_ => true, Both));
        Assert.Single(owed.Due(_ => false, Both));
    }

    [Fact]
    public void SomeoneWhoLeft_IsNotAsked()
    {
        var owed = Owed(Anne, Bas);
        owed.Urge(1, Bas, 1);
        owed.Ended(1, Anne);
        owed.Ended(1, Bas);

        Assert.Equal(Anne, Assert.Single(owed.Due(_ => false, new[] { Self(Anne) })).Who);
    }

    [Fact]
    public void ARepromptCut_ForgetsWhatWasCut()
    {
        var owed = Owed(Anne);
        owed.Forget(0);

        Assert.False(owed.Owes(1));
    }

    [Fact]
    public void NobodyListening_NothingOwed()
    {
        var owed = new OwedAnswers(TimeSpan.FromMinutes(10));
        owed.Expect(1, Human, new[] { Human }, 0);

        Assert.False(owed.Owes(1));
    }

    [Fact]
    public void ALineNobodyFinishedWith_IsLetGoInTime()
    {
        var owed = Owed(Anne);
        owed.Expect(5, Human, new[] { Bas }, (long)TimeSpan.FromMinutes(11).TotalMilliseconds);

        Assert.False(owed.Owes(1));
        Assert.True(owed.Owes(5));
    }
}

/// <summary>The Room settles a human's line (ADR 0031, #129): real grains, Persona turns played by hand.</summary>
public class OwedAnswersWorldTest(OwedAnswersWorldTest.Fixture fx) : IClassFixture<OwedAnswersWorldTest.Fixture>
{
    private IGrainFactory Grains => fx.GrainFactory;

    private async Task<(Guid Party, Guid Human, Guid Anne, Guid Bas, Guid Cas)> WorldAsync()
    {
        var (party, human, anne, bas, cas) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var root = Grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        await root.AddPersona(new Persona(human, "Tim", "A human.", null));
        await root.AddPersona(new Persona(anne, "Anne", "You are Anne.", "A baker."));
        await root.AddPersona(new Persona(bas, "Bas", "You are Bas.", "A cyclist."));
        await root.AddPersona(new Persona(cas, "Cas", "You are Cas.", "A plumber."));
        var participants = new List<PartyParticipant>
        {
            new() { Id = human, Name = "Tim", Driver = DriverKind.User },
            new() { Id = anne, Name = "Anne", Driver = DriverKind.LLM },
            new() { Id = bas, Name = "Bas", Driver = DriverKind.LLM },
            new() { Id = cas, Name = "Cas", Driver = DriverKind.LLM },
        };
        await Grains.GetGrain<IPartyRootGrain>(Guid.Empty).AddParty(new PartyInfo { Id = party, Name = "world", Participants = participants });
        await Grains.GetGrain<IPartyGrain>(party).SetParticipants(participants);
        return (party, human, anne, bas, cas);
    }

    private static IEnumerable<PersonaCalls.Call> Answers(Guid room) => PersonaCalls.Calls.Where(c => c.Method == "AnswerAsync" && c.Room == room);

    private static async Task<List<Guid>> HeardByAsync(Guid room, int line, int expected)
    {
        await TestWait.UntilAsync(() => Task.FromResult(PersonaCalls.Calls.Count(c => c.Method == "NotifyMessageAsync" && c.Room == room && c.MessageId == line) >= expected), TimeSpan.FromSeconds(5));
        return PersonaCalls.Calls.Where(c => c.Method == "NotifyMessageAsync" && c.Room == room && c.MessageId == line).Select(c => c.PersonaId).ToList();
    }

    /// <summary>What the Response pipeline does when the Decision phase says no.</summary>
    private static async Task DeclineAsync(IChatGroupGrain room, Guid who, int line, double urge)
    {
        var turn = await room.BeginTurnAsync(who, line, urge);
        await room.EndTurnAsync(turn, new TurnOutcome.Declined("""{"reason":"I'm not rising to it","stop":true}"""));
    }

    private static async Task NeverAsync(Func<bool> happened)
    {
        await Task.Delay(600);
        Assert.False(happened());
    }

    private async Task<Guid> LobbyTalkAsync(Guid party, params (Guid Who, string? Spot)[] bodies)
    {
        var places = Grains.GetGrain<IPlacesGrain>(party);
        var lobby = (await places.GetSnapshotAsync()).Places.First(p => p.Kind == PlaceKinds.Public);
        var spot = lobby.Spots[0].Id;
        foreach (var (who, at) in bodies) await places.MoveAsync(who, lobby.Id, at ?? spot);
        fx.Time.Advance(TimeSpan.FromMinutes(1));
        return (await places.GetSnapshotAsync()).Places.First(p => p.Id == lobby.Id).TalkRoomId!.Value;
    }

    [Fact]
    public async Task Place_EveryoneWhoHeardItDeclines_TheKeenestOfThemAnswers_NotWhoeverDidntHearIt()
    {
        var (party, human, anne, bas, cas) = await WorldAsync();
        var places = Grains.GetGrain<IPlacesGrain>(party);
        var lobby = (await places.GetSnapshotAsync()).Places.First(p => p.Kind == PlaceKinds.Public);
        var talk = await LobbyTalkAsync(party, (human, null), (anne, null), (bas, null), (cas, lobby.Spots[^1].Id));
        var room = Grains.GetGrain<IChatGroupGrain>(talk);

        var line = await room.SendNewMessageAsync(human, "anyone know where the bread went", null, null);
        var heard = await HeardByAsync(talk, line.MessageId, 2);
        Assert.DoesNotContain(cas, heard);
        Assert.DoesNotContain(cas, line.HeardBy!);

        await DeclineAsync(room, anne, line.MessageId, 0.5);
        Assert.Empty(Answers(talk));
        await room.RecordSkippedTurnAsync(bas, "Bas", line.MessageId, 0.8, "reflex: listen");

        await TestWait.UntilAsync(() => Task.FromResult(Answers(talk).Any()), TimeSpan.FromSeconds(5));
        var answer = Assert.Single(Answers(talk));
        Assert.Equal(bas, answer.PersonaId);
        Assert.Equal(line.MessageId, answer.MessageId);
    }

    [Fact]
    public async Task Dm_TheDecisionPhaseDeclinesAHuman_TheyAreAskedToAnswerAnyway()
    {
        var (party, human, anne, _, _) = await WorldAsync();
        var dm = await Grains.GetGrain<IPartyGrain>(party).GetOrCreateDirectChatAsync(human, anne);
        var room = Grains.GetGrain<IChatGroupGrain>(dm.Id);

        var line = await room.SendNewMessageAsync(human, "you around?", null, null);
        await HeardByAsync(dm.Id, line.MessageId, 1);
        await DeclineAsync(room, anne, line.MessageId, 1);

        await TestWait.UntilAsync(() => Task.FromResult(Answers(dm.Id).Any()), TimeSpan.FromSeconds(5));
        Assert.Equal(anne, Assert.Single(Answers(dm.Id)).PersonaId);
    }

    [Fact]
    public async Task NoDoubleRerun_TheRerunsOwnTurnAndStrayOutcomesDontAskAgain()
    {
        var (party, human, anne, _, _) = await WorldAsync();
        var dm = await Grains.GetGrain<IPartyGrain>(party).GetOrCreateDirectChatAsync(human, anne);
        var room = Grains.GetGrain<IChatGroupGrain>(dm.Id);
        var line = await room.SendNewMessageAsync(human, "you around?", null, null);
        await HeardByAsync(dm.Id, line.MessageId, 1);
        await DeclineAsync(room, anne, line.MessageId, 1);
        await TestWait.UntilAsync(() => Task.FromResult(Answers(dm.Id).Any()), TimeSpan.FromSeconds(5));

        await DeclineAsync(room, anne, line.MessageId, 1);
        await room.RecordSkippedTurnAsync(anne, "Anne", line.MessageId, 1, "obvious-skip");

        await NeverAsync(() => Answers(dm.Id).Count() > 1);
    }

    [Fact]
    public async Task SomethingSaid_ReflexHolding_OrAFailure_MeansNoRerun()
    {
        var (party, human, anne, bas, _) = await WorldAsync();
        var talk = await LobbyTalkAsync(party, (human, null), (anne, null), (bas, null));
        var room = Grains.GetGrain<IChatGroupGrain>(talk);

        var spoken = await room.SendNewMessageAsync(human, "morning", null, null);
        await HeardByAsync(talk, spoken.MessageId, 2);
        var turn = await room.BeginTurnAsync(anne, spoken.MessageId, 0.5);
        await room.EndTurnAsync(turn, new TurnOutcome.Spoke("morning!", null, null, null));
        await DeclineAsync(room, bas, spoken.MessageId, 0.5);

        var held = await room.SendNewMessageAsync(human, "you two busy?", null, null);
        await HeardByAsync(talk, held.MessageId, 2);
        await DeclineAsync(room, anne, held.MessageId, 0.5);
        await room.RecordSkippedTurnAsync(bas, "Bas", held.MessageId, 0.5, "reflex: holding: a quick line now, properly later");

        var stopped = await room.SendNewMessageAsync(human, "hello??", null, null);
        await HeardByAsync(talk, stopped.MessageId, 2);
        await DeclineAsync(room, anne, stopped.MessageId, 0.5);
        var cut = await room.BeginTurnAsync(bas, stopped.MessageId, 0.5);
        await room.EndTurnAsync(cut, new TurnOutcome.Failed("cancelled"));

        await NeverAsync(() => Answers(talk).Any());
    }

    [Fact]
    public async Task ALineFromAPersona_OrAPlayerOnAuto_IsNeverRerun()
    {
        var (party, human, anne, bas, _) = await WorldAsync();
        var partyGrain = Grains.GetGrain<IPartyGrain>(party);
        var clock = Grains.GetGrain<IWorldClockGrain>(party);
        var player = (await clock.ClaimPlayerAsync($"user_{Guid.NewGuid():N}", $"Sparky{Guid.NewGuid():N}"[..12]))!.Value;
        var link = (await partyGrain.GetParty()).Participants.Single(p => p.Id == player);
        await partyGrain.UpsertParticipantsAsync(new List<PartyParticipant> { link with { Driver = DriverKind.LLM } });
        Assert.True(await Grains.IsOnAutoAsync(party, player));
        var talk = await LobbyTalkAsync(party, (human, null), (anne, null), (bas, null), (player, null));
        var room = Grains.GetGrain<IChatGroupGrain>(talk);

        foreach (var from in new[] { anne, player })
        {
            var line = await room.SendNewMessageAsync(from, "anyone up for chess", null, null);
            var heard = await HeardByAsync(talk, line.MessageId, 2);
            foreach (var who in heard.Where(h => h != human))
                await DeclineAsync(room, who, line.MessageId, 0.5);
        }

        await NeverAsync(() => Answers(talk).Any());
    }

    /// <summary>Records and swallows Persona turns, so the test plays them by hand.</summary>
    public sealed class PersonaCalls : IIncomingGrainCallFilter
    {
        public record struct Call(Guid PersonaId, string Method, Guid Room, int? MessageId);

        private static readonly string[] Swallowed = { "NotifyMessageAsync", "AnswerAsync", "InitiateAsync" };
        private static readonly ConcurrentQueue<Call> Queue = new();

        public static IReadOnlyCollection<Call> Calls => Queue;

        public async Task Invoke(IIncomingGrainCallContext context)
        {
            if (context.InterfaceMethod?.DeclaringType == typeof(IPersonaGrain) && Swallowed.Contains(context.InterfaceMethod.Name))
            {
                var who = context.Grain is Grain g ? g.GetPrimaryKey() : Guid.Empty;
                var room = context.Request.GetArgument(0) is Guid r ? r : Guid.Empty;
                var line = context.Request.GetArgumentCount() > 1 && context.Request.GetArgument(1) is ChatMessage m ? m.MessageId : (int?)null;
                Queue.Enqueue(new Call(who, context.InterfaceMethod.Name, room, line));
                return;
            }
            await context.Invoke();
        }
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private TestCluster _cluster = default!;

        public IGrainFactory GrainFactory => _cluster.GrainFactory;

        public Noon Time => Noon.Instance;

        public async Task InitializeAsync()
        {
            var builder = new TestClusterBuilder { Options = { InitialSilosCount = 1 } };
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            _cluster = builder.Build();
            await _cluster.DeployAsync();
        }

        public async Task DisposeAsync() => await _cluster.DisposeAsync();

        private sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder.ConfigureDefaults().AddIncomingGrainCallFilter<PersonaCalls>();
                siloBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<TimeProvider>(Noon.Instance);
                    services.AddSingleton(Mock.Of<IWorldLlm>());
                    services.AddSingleton(Mock.Of<IStanceConsolidator>());
                    services.AddSingleton<ConsolidationService>();
                    services.AddSingleton<WorldMemory>();
                    services.AddSingleton<PartyTown.World.Work.Workplaces>();
                    services.AddSingleton<PartyTown.World.Work.Responsibilities>();
                });
            }
        }
    }
}
