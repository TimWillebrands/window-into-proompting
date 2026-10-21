using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Services.Memory;

namespace BackendTest;

/// <summary>Each half of a beat's recall fails on its own; cancellation still propagates.</summary>
public class RecallBeatTest
{
    private static readonly BeatAnchors At = new(Guid.NewGuid(), Guid.NewGuid(), new[] { Guid.NewGuid() }, "lisp", Guid.NewGuid());

    [Fact]
    public async Task RecallFailure_KeepsStances_AndPassesThePlace()
    {
        var repo = new Mock<IMemoryRepository>();
        repo.Setup(r => r.RecallAsync(At.PersonaId, At.PartyId, At.PresentIds, "lisp", 8, It.IsAny<CancellationToken>(), At.PlaceId))
            .ThrowsAsync(new InvalidOperationException("db down"));
        repo.Setup(r => r.RecallStancesAsync(At.PartyId, At.PersonaId, At.PresentIds, "lisp", 6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new StanceLine("lisp is elegant", 0.7) });

        var beat = await repo.Object.RecallBeatAsync(At, 8, 6, NullLogger.Instance, CancellationToken.None);

        Assert.Empty(beat.Memories);
        Assert.Equal("lisp is elegant", Assert.Single(beat.Stances).Reasoning);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var repo = new Mock<IMemoryRepository>();
        repo.Setup(r => r.RecallAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repo.Object.RecallBeatAsync(At, 8, 6, NullLogger.Instance, cts.Token));
    }
}
