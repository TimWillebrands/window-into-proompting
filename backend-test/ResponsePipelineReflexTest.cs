using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Services.ResponsePipeline;
using PartyTown.World;
using PartyTown.World.Reflex;

namespace BackendTest;

/// <summary>How the seam behaves for its callers: a failing Reflex never costs a reply; a burst gets one reply (ADR 0031).</summary>
public class ReflexGateTest
{
    private static readonly Percept Line = new(PerceptKind.Direct, Guid.NewGuid(), "Hana", false, "hi", Guid.NewGuid());

    private static ReflexGate Gate(Mock<IPersonaReflexGrain> grain)
    {
        var grains = new Mock<IGrainFactory>();
        grains.Setup(g => g.GetGrain<IPersonaReflexGrain>(It.IsAny<string>(), null)).Returns(grain.Object);
        return new ReflexGate(grains.Object, NullLogger<ReflexGate>.Instance);
    }

    [Fact]
    public async Task Perceive_WhenTheGrainFails_RepliesAsUsual()
    {
        var grain = new Mock<IPersonaReflexGrain>();
        grain.Setup(g => g.PerceiveAsync(It.IsAny<Percept>())).ThrowsAsync(new InvalidOperationException("boom"));

        var verdict = await Gate(grain).PerceiveAsync(Guid.NewGuid(), Guid.NewGuid(), Line);

        Assert.True(verdict.Proceed);
    }

    [Fact]
    public async Task Mind_WhenTheGrainFails_IsEmpty()
    {
        var grain = new Mock<IPersonaReflexGrain>();
        grain.Setup(g => g.GetMindAsync()).ThrowsAsync(new InvalidOperationException("boom"));

        Assert.Equal(string.Empty, await Gate(grain).MindAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Read_UninterruptedLine_GoesAhead()
        => Assert.True(await new InFlightStore().ReadAsync(Guid.NewGuid(), TimeSpan.FromMilliseconds(5), CancellationToken.None));

    [Fact]
    public async Task Read_NewerLineInTheSameChat_TakesOver()
    {
        var store = new InFlightStore();
        var room = Guid.NewGuid();

        var first = store.ReadAsync(room, TimeSpan.FromSeconds(30), CancellationToken.None);
        var second = store.ReadAsync(room, TimeSpan.FromMilliseconds(5), CancellationToken.None);

        Assert.False(await first);
        Assert.True(await second);
    }

    [Fact]
    public async Task Read_OtherChats_DoNotInterfere()
    {
        var store = new InFlightStore();

        var first = store.ReadAsync(Guid.NewGuid(), TimeSpan.FromMilliseconds(30), CancellationToken.None);
        var second = store.ReadAsync(Guid.NewGuid(), TimeSpan.FromMilliseconds(5), CancellationToken.None);

        Assert.True(await first);
        Assert.True(await second);
    }

    [Fact]
    public async Task Read_CancelledFromOutside_Throws()
    {
        using var cts = new CancellationTokenSource();
        var read = new InFlightStore().ReadAsync(Guid.NewGuid(), TimeSpan.FromSeconds(30), cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }
}
