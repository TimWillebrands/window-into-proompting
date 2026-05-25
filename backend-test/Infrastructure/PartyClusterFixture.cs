using Orleans.Hosting;
using Orleans.TestingHost;

namespace BackendTest.Infrastructure;

/// <summary>
/// Plain Orleans <see cref="TestCluster"/> fixture for tests that need real PartyGrain /
/// PersonaRootGrain / PersonaGrain activation without the <see cref="FanoutInterceptor"/>
/// short-circuit. Use this when the test exercises party/persona state, not fanout flow.
///
/// <para>(<see cref="ChatGroupClusterFixture"/> adds <see cref="FanoutInterceptor"/> which
/// throws "broken filter" in this Orleans build for any call that takes the short-circuit
/// branch — so persona-activating tests have to opt out of that filter.)</para>
/// </summary>
public sealed class PartyClusterFixture : IAsyncLifetime
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

    public Task DisposeAsync() => Cluster.DisposeAsync().AsTask();

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder) => siloBuilder.ConfigureDefaults();
    }
}
