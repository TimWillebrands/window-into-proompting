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

var probes = ProbeRegistry.Discover();
var command = args.Length > 0 ? args[0] : "list";

if (command is "list" or "--list" or "-l")
{
    PrintProbes(probes);
    return 0;
}

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

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "benchsettings.json"), optional: false)
    .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "benchsettings.local.json"), optional: true)
    .AddEnvironmentVariables();

builder.Services.AddLlmProviderOptions();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

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
    var llmResponseTimeout = TimeSpan.FromMinutes(3);
    silo
        .UseLocalhostClustering(siloPort: 11411, gatewayPort: 30411)
        .Configure<SiloMessagingOptions>(o => o.ResponseTimeout = llmResponseTimeout)
        .Configure<ClientMessagingOptions>(o => o.ResponseTimeout = llmResponseTimeout)
        .AddMemoryGrainStorage("parties")
        .AddMemoryGrainStorage("personas")
        .AddMemoryGrainStorage("urls")
        .AddMemoryGrainStorage("PubSubStore")
        .AddStateStorageBasedLogConsistencyProvider("PartyStateStorage")
        .AddMemoryStreams("party-streams")
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
