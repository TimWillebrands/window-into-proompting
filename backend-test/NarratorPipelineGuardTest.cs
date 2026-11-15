using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest;

public class NarratorPipelineGuardTest
{
    private static PartyParticipant Llm(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, Driver = DriverKind.LLM };

    private static PartyParticipant Human(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, Driver = DriverKind.User };

    private static PartyParticipant System(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, Driver = DriverKind.System };

    [Fact]
    public void CancelTargets_SkipSystem_KeepUserDefaults()
    {
        var alice = Llm("Alice");
        var narrator = System("Narrator");
        var human = Human("You");

        var targets = PartyGrain.SelectCancelTargets(new[] { alice, narrator, human }).Select(p => p.Id).ToList();

        Assert.Equal(new[] { alice.Id, human.Id }, targets);
    }
}
