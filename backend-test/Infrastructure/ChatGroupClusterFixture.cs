using System.Collections.Concurrent;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.TestingHost;
using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest.Infrastructure;

/// <summary>
/// Orleans <see cref="TestCluster"/> fixture for grain-level fanout/feedback-loop tests.
///
/// Why a real TestCluster instead of OrleansTestKit: ChatGroupGrain + PartyGrain are
/// JournaledGrains that need <c>ILogConsistencyProvider "PartyStateStorage"</c> wired
/// via <c>ISiloBuilder</c>. TestKit v10 doesn't expose the silo builder, so we use the
/// real in-proc cluster with memory storage.
///
/// Fanout interception: a <see cref="FanoutInterceptor"/> (incoming grain call filter)
/// records every <c>IPersonaGrain.NotifyMessageAsync</c> invocation and short-circuits
/// the call so the real PersonaGrain body never runs. Tests read
/// <see cref="FanoutInterceptor.Calls"/> to assert fanout behavior.
/// </summary>
public sealed class ChatGroupClusterFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;
    public IGrainFactory GrainFactory => Cluster.GrainFactory;

    public Task InitializeAsync()
    {
        var builder = new TestClusterBuilder(1);
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        Cluster = builder.Build();
        return Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        FanoutInterceptor.Reset();
        await Cluster.DisposeAsync();
    }

    public async Task<(Guid partyId, Guid chatGroupId, List<PartyParticipant> participants)>
        CreatePartyWithChatGroupAsync(params string[] personaNames)
    {
        var participants = personaNames
            .Select(n => new PartyParticipant { Id = Guid.NewGuid(), Name = n, Driver = DriverKind.LLM })
            .ToList();
        return await CreatePartyWithExplicitParticipantsAsync(participants);
    }

    /// <summary>
    /// Same as <see cref="CreatePartyWithChatGroupAsync"/> but lets the caller supply a fully-built
    /// participant list. Returned <c>participants</c> is the original input — the grain may have
    /// added invariants like the Narrator-Participant on top (ADR 0012), but tests typically
    /// only want to reason about the cast they passed in.
    /// </summary>
    public async Task<(Guid partyId, Guid chatGroupId, List<PartyParticipant> participants)>
        CreatePartyWithExplicitParticipantsAsync(List<PartyParticipant> participants)
    {
        var partyId = Guid.NewGuid();

        var root = GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await root.AddParty(new PartyInfo { Id = partyId, Name = "test-party", Participants = participants });

        var party = GrainFactory.GetGrain<IPartyGrain>(partyId);
        await party.SetParticipants(participants);
        var chatGroup = await party.CreateChatGroup("test-group");

        return (partyId, chatGroup.Id, participants);
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureDefaults()
                .AddIncomingGrainCallFilter<FanoutInterceptor>();
        }
    }
}

/// <summary>
/// Incoming grain call filter: records every call targeting <see cref="IPersonaGrain"/>
/// and short-circuits without invoking the real grain method. Tests enumerate
/// <see cref="Calls"/> to assert fanout count/ordering/distribution.
/// </summary>
public sealed class FanoutInterceptor : IIncomingGrainCallFilter
{
    public record struct Call(Guid PersonaId, string Method, Guid ChatGroupId, int? MessageId, DateTimeOffset At);

    private static readonly ConcurrentQueue<Call> _calls = new();

    public static IReadOnlyCollection<Call> Calls => _calls;
    public static void Reset() => _calls.Clear();

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.InterfaceMethod?.DeclaringType == typeof(IPersonaGrain))
        {
            var personaId = context.Grain is Grain g ? g.GetPrimaryKey() : Guid.Empty;
            var chatGroupId = context.Request.GetArgument(0) is Guid cg ? cg : Guid.Empty;
            var msgId = context.Request.GetArgument(1) is ChatMessage m ? m.MessageId : (int?)null;
            _calls.Enqueue(new Call(personaId, context.InterfaceMethod.Name, chatGroupId, msgId, DateTimeOffset.UtcNow));
            return; // short-circuit — never invoke real persona grain
        }

        await context.Invoke();
    }
}
