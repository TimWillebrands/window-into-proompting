using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest;

/// <summary>
/// Asserts the response-pipeline guard around <see cref="DriverKind.System"/>: a Participant
/// whose Driver is <c>System</c> is never selected for auto-respond fanout, regardless of who
/// triggered the message. Issue #75 / ADR 0012.
///
/// Tests target <see cref="ChatGroupGrain.SelectAutoRespondTargets"/> directly — the same
/// pure filter <c>NotifyAllParticipantsAsync</c> uses — rather than driving fanout through the
/// full TestCluster, because the TestCluster-based fanout interceptor pattern is flaky locally
/// (see the file-header note in <c>PersonaGrainFeedbackTest.cs</c>).
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
    public void SystemDrivenParticipant_NeverSelected_WhenUserTriggered()
    {
        var alice = Llm("Alice");
        var narrator = System("Narrator");
        var human = Human("You");

        var targets = ChatGroupGrain.SelectAutoRespondTargets(
            new[] { alice, narrator, human },
            senderId: human.Id);

        Assert.Single(targets);
        Assert.Equal(alice.Id, targets[0].Id);
        Assert.DoesNotContain(targets, p => p.Driver == DriverKind.System);
    }

    [Fact]
    public void SystemDrivenParticipant_NeverSelected_EvenWhenSomeoneMentionsThem()
    {
        // "Urge spike" condition: doesn't matter — the guard is structural, applied before
        // urge math is even reached. (Urge is computed per-Persona inside PersonaGrain; if
        // the Persona never receives a NotifyMessageAsync, the urge code never runs.)
        var alice = Llm("Alice");
        var bob = Llm("Bob");
        var narrator = System("Narrator");

        var targets = ChatGroupGrain.SelectAutoRespondTargets(
            new[] { alice, bob, narrator },
            senderId: alice.Id); // Alice excluded as sender; narrator excluded as System.

        Assert.Single(targets);
        Assert.Equal(bob.Id, targets[0].Id);
        Assert.DoesNotContain(targets, p => p.Id == narrator.Id);
    }

    [Fact]
    public void Sender_IsExcluded()
    {
        var alice = Llm("Alice");
        var bob = Llm("Bob");

        var targets = ChatGroupGrain.SelectAutoRespondTargets(
            new[] { alice, bob }, senderId: alice.Id);

        Assert.Single(targets);
        Assert.Equal(bob.Id, targets[0].Id);
    }

    [Fact]
    public void UserDrivenParticipant_IsExcluded()
    {
        var alice = Llm("Alice");
        var human = Human("You");

        var targets = ChatGroupGrain.SelectAutoRespondTargets(
            new[] { alice, human }, senderId: Guid.NewGuid());

        Assert.Single(targets);
        Assert.Equal(alice.Id, targets[0].Id);
    }

    [Fact]
    public void NullSender_IncludesAllLLMs()
    {
        // No sender filter (e.g., system-triggered broadcast): every LLM-driven Participant
        // is in scope; User and System are still excluded.
        var alice = Llm("Alice");
        var bob = Llm("Bob");
        var narrator = System("Narrator");
        var human = Human("You");

        var targets = ChatGroupGrain.SelectAutoRespondTargets(
            new[] { alice, bob, narrator, human }, senderId: null);

        Assert.Equal(2, targets.Count);
        Assert.All(targets, p => Assert.Equal(DriverKind.LLM, p.Driver));
    }

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
