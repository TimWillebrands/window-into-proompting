using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using PartyTown.Bench;
using PartyTown.Configuration;
using PartyTown.Services.Memory;

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
// Mandatory even though decision probes call the service directly — any host that can activate
// PersonaGrain hangs silently without an IMemoryRepository registration.
builder.Services.AddSingleton<IMemoryRepository, StubMemoryRepository>();

builder.UseOrleans(silo =>
{
    silo
        // Off-default ports: must not collide with a running Aspire silo (11111/30000).
        .UseLocalhostClustering(siloPort: 11411, gatewayPort: 30411)
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
        await Doctor.RunAsync(grains);
        return 0;
    }

    var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
    var artifact = new ProbeArtifact(probe!.Name, DateTimeOffset.UtcNow);

    LlmCallRecorder.Reset();
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    var bench = new Bench(grains, loggerFactory, artifact, cts.Token);
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
}

static void PrintProbes(IReadOnlyList<ProbeInfo> probes)
{
    Console.WriteLine($"\nProbes ({probes.Count}):\n");
    foreach (var p in probes)
        Console.WriteLine($"  {p.Name,-26} {p.Description}");
    Console.WriteLine("\nUsage: dotnet run --project tools/bench -- <probe|doctor|list>\n");
}
