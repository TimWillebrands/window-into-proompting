using Moq;
using Orleans.TestKit;
using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest.Fakes;

/// <summary>
/// Scripted fake <see cref="IPersonaGrain"/> that:
///   1. Records every <c>NotifyMessageAsync</c> call with the triggering message and chat group.
///   2. Drives the feedback loop by calling back into the <see cref="IChatGroupGrain"/> probe —
///      the same contract the real <c>PersonaGrain</c> upholds.
///
/// Scripting is via a queue: each <c>Script*()</c> call enqueues one behaviour for the
/// next <c>NotifyMessageAsync</c> invocation. When the queue is exhausted, subsequent calls
/// default to <see cref="ScriptDecline"/>.
///
/// Usage:
/// <code>
///   var fake = new FakePersonaGrain(personaId, Silo)
///       .ScriptRespond("hello!")     // first call: respond
///       .ScriptDecline("quiet now"); // second call: decline
///   fake.RegisterOn();
///   // ... trigger fanout ...
///   Assert.Equal(1, fake.Calls.Count);
/// </code>
/// </summary>
public sealed class FakePersonaGrain
{
    public sealed record NotifyCall(Guid ChatGroupId, ChatMessage TriggeringMessage);

    private enum Script { Respond, Decline, Fail, Hang }

    private readonly Queue<(Script script, string data)> _script = new();
    private readonly TestKitSilo _silo;

    public Guid Id { get; }
    public string Name { get; }

    /// <summary>All NotifyMessageAsync calls received, in order.</summary>
    public List<NotifyCall> Calls { get; } = new();

    /// <summary>The TestKit grain proxy — compare by reference when asserting fanout targets.</summary>
    public IPersonaGrain Reference { get; private set; } = null!;

    public FakePersonaGrain(Guid id, TestKitSilo silo, string name = "TestPersona")
    {
        Id = id;
        Name = name;
        _silo = silo;
    }

    // ── script builders ───────────────────────────────────────────────────────

    /// <summary>Next notification: respond by calling <c>AppendMessageAsync(content)</c>.</summary>
    public FakePersonaGrain ScriptRespond(string content = "hello") { _script.Enqueue((Script.Respond, content)); return this; }

    /// <summary>Next notification: decline by calling <c>MarkGenerationStoppedAsync(reason)</c>.</summary>
    public FakePersonaGrain ScriptDecline(string reason = "silent") { _script.Enqueue((Script.Decline, reason)); return this; }

    /// <summary>Next notification: fail by calling <c>MarkGenerationFailedAsync(error)</c>.</summary>
    public FakePersonaGrain ScriptFail(string error = "boom") { _script.Enqueue((Script.Fail, error)); return this; }

    /// <summary>Next notification: block until the cancellation token fires (simulates a hanging LLM).</summary>
    public FakePersonaGrain ScriptHang() { _script.Enqueue((Script.Hang, "")); return this; }

    // ── registration ──────────────────────────────────────────────────────────

    public IPersonaGrain RegisterOn()
    {
        var mock = _silo.AddProbe<IPersonaGrain>(Id);
        mock.Setup(g => g.NotifyMessageAsync(
                It.IsAny<Guid>(), It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns((Guid cgId, ChatMessage msg, CancellationToken ct) =>
                HandleAsync(cgId, msg, ct));
        mock.Setup(g => g.CancelGenerationAsync()).Returns(Task.CompletedTask);
        Reference = mock.Object;
        return Reference;
    }

    // ── feedback loop ─────────────────────────────────────────────────────────

    private async Task HandleAsync(Guid chatGroupId, ChatMessage msg, CancellationToken ct)
    {
        Calls.Add(new(chatGroupId, msg));

        var (script, data) = _script.Count > 0
            ? _script.Dequeue()
            : (Script.Decline, "FakePersonaGrain: queue exhausted");

        var grain = _silo.GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);
        var msgId = await grain.GetNextMessageIdAsync(Id);

        switch (script)
        {
            case Script.Respond:
                await grain.AppendMessageAsync(msgId, data, null, null, ct);
                break;
            case Script.Decline:
                await grain.MarkGenerationStoppedAsync(msgId, data);
                break;
            case Script.Fail:
                await grain.MarkGenerationFailedAsync(msgId, data);
                break;
            case Script.Hang:
                await Task.Delay(Timeout.Infinite, ct);
                break;
        }
    }
}
