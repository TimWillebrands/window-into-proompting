using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest;

/// <summary>
/// Asserts the cancel-fanout filter around <see cref="DriverKind.System"/>: a Participant
/// whose Driver is <c>System</c> (the Narrator) is never targeted by
/// <see cref="PartyGrain.CancelAllGenerations"/> because it never has an in-flight pipeline
/// to cancel in the first place. Issue #75 / ADR 0012.
///
/// The auto-respond fanout filter moved to the response pipeline itself — see
/// <c>ResponsePipeline.HandleAsync</c>'s <c>selfEffective != LLM</c> short-circuit — and is
/// covered by <see cref="ResponsePipelineTest"/>.
/// </summary>
public class NarratorPipelineGuardTest
{
    private static PartyParticipant Llm(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, Driver = DriverKind.LLM };

    private static PartyParticipant Human(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, Driver = DriverKind.User };

    private static PartyParticipant System(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, Driver = DriverKind.System };

    [Fact]
    public void CancelTargets_OnlyLLMs()
    {
        // CancelAllGenerations must skip System and User — Narrator has no in-flight generation
        // to cancel (it never speaks), and the User isn't a grain we'd cancel.
        var alice = Llm("Alice");
        var narrator = System("Narrator");
        var human = Human("You");

        var targets = PartyGrain.SelectCancelTargets(new[] { alice, narrator, human }).ToList();

        Assert.Single(targets);
        Assert.Equal(alice.Id, targets[0].Id);
    }
}
