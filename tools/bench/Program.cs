using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using PartyTown.Bench;
using PartyTown.Configuration;
using PartyTown.Services.Memory;
using Testcontainers.PostgreSql;

// The Bench (ADR 0011): a headless console host that runs Probes against the real grain path
// and emits Probe Artifacts. `list`/no-args needs no host; `doctor` and probe runs spin up an
// in-memory Orleans silo on off-default ports so the bench coexists with a running Aspire stack.

var probes = ProbeRegistry.Discover();
var command = args.Length > 0 ? args[0] : "list";

if (command is "list" or "--list" or "-l")
{
    PrintProbes(probes);
    return 0;
}

// Resolve the probe before paying for a silo.
ProbeInfo? probe = null;
if (command is not "doctor")
{
    probe = probes.FirstOrDefault(p => string.Equals(p.Name, command, StringComparison.OrdinalIgnoreCase));
    if (probe is null)
    {
        Console.Error.WriteLine($"Unknown probe '{command}'.");
        PrintProbes(probes);
        return 1;
    }
}

// Real memory is opt-in per probe (ADR 0011 amendment): only a RequiresMemory probe — or `doctor`,
// which reports the tier — pays for the ephemeral AGE container. Prompt-composition probes stay
// Docker-free (tier-0). Docker absent when a probe needs memory → loud fall back to the stub.
var wantsMemory = command is "doctor" || (probe?.RequiresMemory ?? false);
PostgreSqlContainer? postgres = null;
string? memoryConnectionString = null;
string memoryStatus;

if (wantsMemory)
{
    try
    {
        Console.Error.WriteLine("memory: starting ephemeral AGE container (first run pulls the image)…");
        postgres = BenchMemory.NewContainer();
        await postgres.StartAsync();
        memoryConnectionString = postgres.GetConnectionString();
        await BenchMemory.PrepareSchemaAsync(memoryConnectionString, CancellationToken.None);
        memoryStatus = "LIVE (testcontainers)";
    }
    catch (Exception ex)
    {
        memoryStatus = $"stubbed (no docker: {ex.Message})";
        memoryConnectionString = null;
        if (postgres is not null)
        {
            await postgres.DisposeAsync();
            postgres = null;
        }
        if (probe?.RequiresMemory == true)
            Console.Error.WriteLine(
                $"!! probe '{probe.Name}' needs real memory but the AGE container could not start — " +
                "falling back to StubMemoryRepository. Recall/Stance reads will be empty, NOT a real loop.");
    }
}
else
{
    memoryStatus = "stubbed (not required by this probe)";
}

var builder = Host.CreateApplicationBuilder();

// benchsettings.json ships beside the binary (copied to output); benchsettings.local.json is an
// optional cwd-relative override (gitignored, for a developer's local provider tweaks).
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "benchsettings.json"), optional: false)
    .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "benchsettings.local.json"), optional: true)
    .AddEnvironmentVariables();

builder.Services.AddLlmProviderOptions();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

// An IMemoryRepository registration is mandatory even for decision probes that call the service
// directly — any host that can activate PersonaGrain hangs silently without one. RequiresMemory
// probes (with Docker up) get the real repo over the bench's AGE container, wired exactly like
// production (backend/Program.cs); everything else gets the no-op stub.
if (memoryConnectionString is not null)
{
    builder.Services.AddSingleton<IMemoryExtractor, MemoryExtractor>();
    builder.Services.AddSingleton(BenchMemory.CreateDbContextFactory(memoryConnectionString));
    builder.Services.AddSingleton<IMemoryRepository, MemoryRepository>();
}
else
{
    builder.Services.AddSingleton<IMemoryRepository, StubMemoryRepository>();
}

builder.UseOrleans(silo =>
{
    // LLM one-shot calls routinely exceed Orleans' default 30s response timeout,
    // so the co-hosted client's request deadline fires and the in-flight response
    // is dropped as expired. Give the grain<->client RPC generous headroom.
    var llmResponseTimeout = TimeSpan.FromMinutes(3);
    // Off-default ports: must not collide with a running Aspire silo (11111/30000).
    // BENCH_PORT_OFFSET shifts both so parallel bench processes (e.g. side-by-side model
    // comparison runs) don't fight over the same sockets.
    var portOffset = int.TryParse(Environment.GetEnvironmentVariable("BENCH_PORT_OFFSET"), out var po) ? po : 0;
    silo
        .UseLocalhostClustering(siloPort: 11411 + portOffset, gatewayPort: 30411 + portOffset)
        .Configure<SiloMessagingOptions>(o => o.ResponseTimeout = llmResponseTimeout)
        .Configure<ClientMessagingOptions>(o => o.ResponseTimeout = llmResponseTimeout)
        .AddMemoryGrainStorage("parties")
        .AddMemoryGrainStorage("personas")
        .AddMemoryGrainStorage("urls")
        .AddMemoryGrainStorage("PubSubStore")
        .AddStateStorageBasedLogConsistencyProvider("PartyStateStorage")
        .AddMemoryStreams("party-streams")
        // Captures composed prompts flowing into endpoint grains — see LlmCallRecorder.
        .AddIncomingGrainCallFilter<LlmCallRecorder>();
});

using var host = builder.Build();
await host.StartAsync();

try
{
    var grains = host.Services.GetRequiredService<IGrainFactory>();

    if (command is "doctor")
    {
        await Doctor.RunAsync(grains, memoryStatus);
        return 0;
    }

    var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
    var memory = host.Services.GetRequiredService<IMemoryRepository>();
    var artifact = new ProbeArtifact(probe!.Name, DateTimeOffset.UtcNow);

    LlmCallRecorder.Reset();
    // Probe deadline. The full multi-turn probes (live recall loops) fan out ~15 LLM calls and
    // blow the old 5-min cap on slow local models; default to 15 min and let
    // BENCH_PROBE_TIMEOUT_MIN tune it (down for fast slices, up for heavier runs). Still fires so
    // a hung probe stops instead of racing the artifact write.
    var probeTimeoutMin = int.TryParse(
        Environment.GetEnvironmentVariable("BENCH_PROBE_TIMEOUT_MIN"), out var m) && m > 0 ? m : 15;
    Console.Error.WriteLine($"probe deadline: {probeTimeoutMin} min (BENCH_PROBE_TIMEOUT_MIN to override)");
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(probeTimeoutMin));
    var bench = new Bench(grains, loggerFactory, memory, artifact, cts.Token);
    var exit = 0;
    try
    {
        await probe.Run(bench).WaitAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Probe timed out (5 min).");
        exit = 2;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Probe threw: {ex}");
        artifact.Observe("probe-exception", new { ex.GetType().Name, ex.Message });
        exit = 3;
    }

    artifact.LlmCalls = LlmCallRecorder.Calls.OrderBy(c => c.Seq).ToList();
    var path = artifact.Write();
    artifact.Render(path);
    return exit;
}
finally
{
    await host.StopAsync();
    // Ryuk reaps the container on process exit anyway, but dispose explicitly so a clean run leaves
    // nothing behind (ephemeral-by-default, ADR 0011 amendment).
    if (postgres is not null)
        await postgres.DisposeAsync();
}

static void PrintProbes(IReadOnlyList<ProbeInfo> probes)
{
    Console.WriteLine($"\nProbes ({probes.Count}):\n");
    foreach (var p in probes)
        Console.WriteLine($"  {p.Name,-26} {p.Description}");
    Console.WriteLine("\nUsage: dotnet run --project tools/bench -- <probe|doctor|list>\n");
}
