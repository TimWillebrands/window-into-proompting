using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PartyTown.Configuration;
using PartyTown.World.Work.Works;

namespace BackendTest;

/// <summary>Set WORKS_LIVE_URL and WORKS_LIVE_TOKEN to run against a real Runner, which spends real tokens.</summary>
public sealed class LiveRunnerFactAttribute : FactAttribute
{
    public LiveRunnerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WORKS_LIVE_URL") is null) Skip = "No live Runner configured.";
    }
}

/// <summary>What the Runner really sends (ADR 0039): the fake in WorksWorldTest can't catch the two sides drifting apart.</summary>
public class RunnerContractTest
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string Read = """
    {"events":[{"n":1,"at":1794004851930,"kind":"tool","text":"write docs/works-hello.md"}],
     "run":{"id":"r2a0d0160","commission":"c-e2e-1","kind":"change","status":"proposed","model":"lfm2:24b","capUsd":1,"usd":0,"tokens":8817,
     "baseRef":"main","baseSha":"9a7a7b6","parent":null,"sessionId":"ses_1","report":"done",
     "diff":{"files":1,"added":1,"removed":0,"paths":["docs/works-hello.md"]},"stop":null,"error":null,"proposition":1,
     "checks":[{"name":"backend-tests","passed":false,"seconds":42,"detail":"Failed: 1, Passed: 1066"}],"createdAt":1794004835914,"updatedAt":1794004978735}}
    """;

    [Fact]
    public void Read_CarriesEverythingACommissionTakesOn()
    {
        var read = JsonSerializer.Deserialize<RunnerRead>(Read, Web)!;

        Assert.Equal((1L, "tool"), (read.Events[0].N, read.Events[0].Kind));
        Assert.Equal(("proposed", 8817L, 1, 1), (read.Run.Status, read.Run.Tokens, read.Run.Diff!.Files, read.Run.Proposition));
        Assert.False(read.Run.Checks[0].Passed);
    }

    [Fact]
    public void Status_WithAnUnreadableReserve_IsNull()
    {
        var status = JsonSerializer.Deserialize<RunnerStatus>("""{"busy":false,"reserveUsd":null,"models":[{"id":"lfm2:24b","label":"local","in":0,"out":0}]}""", Web)!;

        Assert.Null(status.ReserveUsd);
        Assert.Equal("lfm2:24b", status.Models[0].Id);
    }

    [Fact]
    public void Propositions_ReadTheirCommissionAndStatus()
    {
        var list = JsonSerializer.Deserialize<List<RunnerProposition>>("""[{"n":1,"commission":"c-e2e-1","title":"Greet","why":"w","author":"Ria","branch":"proposition/1-greet","parent":null,"baseSha":"a","sha":"b","status":"open","note":null,"checks":[]}]""", Web)!;

        Assert.Equal((1, "c-e2e-1", "open"), (list[0].N, list[0].Commission, list[0].Status));
    }

    [Fact]
    public async Task Disabled_ReportsItselfRatherThanThrowingAtStartup()
    {
        var runner = new HttpRunner(new ConfigurationBuilder().Build(), new Tailnets(new ConfigurationBuilder().Build()));

        Assert.False(runner.Enabled);
        await Assert.ThrowsAsync<RunnerException>(() => runner.StatusAsync(CancellationToken.None));
    }

    [LiveRunnerFact]
    public async Task Live_ARunGoesFromStartToDropped()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Works:RunnerUrl"] = Environment.GetEnvironmentVariable("WORKS_LIVE_URL"),
            ["Works:Token"] = Environment.GetEnvironmentVariable("WORKS_LIVE_TOKEN"),
        }).Build();
        var runner = new HttpRunner(config, new Tailnets(config));
        var ct = CancellationToken.None;

        var status = await runner.StatusAsync(ct);
        var run = await runner.StartAsync(Guid.NewGuid(), "change", "Create docs/live.md containing the word live. Read nothing else, then stop.", status.Models[0].Id, 1, ct);
        var read = await runner.ReadAsync(run.Id, 0, ct);
        for (var i = 0; i < 90 && read.Run.Status is "starting" or "working"; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            read = await runner.ReadAsync(run.Id, 0, ct);
        }

        Assert.Equal("idle", read.Run.Status);
        Assert.True(read.Run.Tokens > 0);
        Assert.NotEmpty(read.Events);
        Assert.Equal(1, read.Run.Diff?.Files);
        Assert.Equal("dropped", (await runner.DropAsync(run.Id, ct)).Status);
        Assert.NotNull(await runner.PropositionsAsync(ct));
    }
}
