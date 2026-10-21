using Microsoft.EntityFrameworkCore;
using Orleans.Configuration;
using PartyTown.Configuration;
using PartyTown.Data;
using PartyTown.Logging;
using PartyTown.Services.Memory;
using PartyTown.Services.Realtime;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Skip Orleans + DB wiring when emitting the OpenAPI document at build time —
// the GetDocument.Insider tool starts hosted services, and the silo would try
// to reach Postgres. Controllers + AddOpenApi() are enough for spec emission.
var openApiBuild = Environment.GetEnvironmentVariable("OPENAPI_GENERATE") == "1";

builder.Services.AddLlmProviderOptions();

builder.Services.AddMemoryCache();
builder.Services.AddOutputCache();
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IPartyRealtimeHub, PartyRealtimeHub>();
builder.Services.AddSingleton<PartyTown.Services.Import.ISceneMapService, PartyTown.Services.Import.SceneMapService>();
builder.Services.AddSingleton<PartyTown.Services.Import.IImportRunCoordinator, PartyTown.Services.Import.ImportRunCoordinator>();
builder.Services.AddSingleton<PartyTown.Services.Import.IImportPersonaFinalizeService, PartyTown.Services.Import.ImportPersonaFinalizeService>();
builder.Services.AddSingleton<RaceTrigger>();
builder.Services.AddSingleton<ResponsePipeline>();

if (!openApiBuild)
{
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' not found.");

    builder.Services.AddSingleton<AgeOperatorInterceptor>();
    builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
        options
            .UseNpgsql(connectionString)
            .AddInterceptors(sp.GetRequiredService<AgeOperatorInterceptor>()));

    builder.Services.AddSingleton<IMemoryExtractor, MemoryExtractor>();
    builder.Services.AddSingleton<IMemoryRepository, MemoryRepository>();
    // Import scene commit (ADR 0017 slice 3). DB-backed, so gated with the rest of the
    // data wiring; controllers are never instantiated during OpenAPI emission.
    builder.Services.AddSingleton<IImportMemoryWriter, ImportMemoryWriter>();
    builder.Services.AddSingleton<PartyTown.Services.Import.IImportCorrectionStore, PartyTown.Services.Import.ImportCorrectionStore>();
    builder.Services.AddSingleton<PartyTown.Services.Import.ImportCommitService>();
    builder.Services.AddSingleton<IStanceConsolidator, StanceConsolidator>();
    builder.Services.AddSingleton<ConsolidationService>();

    // World (ADR 0018): Apps are modes of engagement; Life picks among their Affordances.
    builder.Services.AddSingleton<PartyTown.World.IWorldLlm, PartyTown.World.WorldLlm>();
    // System One (ADR 0019): local decision model first, then Jev (TypeSafe or OpenRouter), else LLM emulation.
    builder.Services.AddSingleton<PartyTown.Services.SystemOne.ISystemOneJudge>(sp =>
    {
        var cfg = builder.Configuration;
        var http = sp.GetRequiredService<IHttpClientFactory>();
        var log = sp.GetRequiredService<ILogger<PartyTown.Services.SystemOne.HttpSystemOneJudge>>();
        PartyTown.Services.SystemOne.ISystemOneJudge Http(string name, string url, string? key, string model)
            => new PartyTown.Services.SystemOne.HttpSystemOneJudge(http.CreateClient("systemone"), name, url, key, model, log);

        var judges = new List<PartyTown.Services.SystemOne.ISystemOneJudge>();
        if (cfg["SYSTEM_ONE_LOCAL_URL"] is { Length: > 0 } local)
            judges.Add(Http("local", $"{local.TrimEnd('/')}/v1/systemone", null, cfg["SYSTEM_ONE_LOCAL_MODEL"] ?? "clef-flash"));
        if (cfg["TYPESAFE_API_KEY"] is { Length: > 0 } typesafe)
            judges.Add(Http("typesafe", PartyTown.Services.SystemOne.HttpSystemOneJudge.TypeSafeUrl, typesafe,
                cfg["TYPESAFE_MODEL"] ?? PartyTown.Services.SystemOne.HttpSystemOneJudge.TypeSafeModel));
        if (cfg["OPENROUTER_API_KEY"] is { Length: > 0 } openRouter && cfg.GetValue("SYSTEM_ONE_OPENROUTER", true))
            judges.Add(Http("openrouter-jev", PartyTown.Services.SystemOne.HttpSystemOneJudge.OpenRouterUrl, openRouter,
                cfg["SYSTEM_ONE_OPENROUTER_MODEL"] ?? PartyTown.Services.SystemOne.HttpSystemOneJudge.OpenRouterModel));
        if (judges.Count == 0)
            return ActivatorUtilities.CreateInstance<PartyTown.Services.SystemOne.LlmJudge>(sp);
        return new PartyTown.Services.SystemOne.FailoverJudge(judges,
            sp.GetRequiredService<ILogger<PartyTown.Services.SystemOne.FailoverJudge>>());
    });
    builder.Services.AddHttpClient("systemone", c => c.Timeout = TimeSpan.FromSeconds(20));
    builder.Services.AddSingleton<PartyTown.World.WorldMemory>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.RoomsApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.MessengerApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.BoardApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.DiaryApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.CalendarApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Places.PlacesApp>();

    // LLM one-shot calls (memory extraction, stance consolidation) routinely run
    // longer than Orleans' default 30s response timeout, which would otherwise drop
    // the in-flight response as expired. Give the grain<->client RPC generous headroom.
    var llmResponseTimeout = TimeSpan.FromMinutes(3);

    // Grain storage JSON must keep default values: enums/bools whose zero differs from the
    // property initializer (DriverKind.User, IsEnabled=false) otherwise reload as the initializer.
    builder.Services.Configure<Orleans.Serialization.OrleansJsonSerializerOptions>(o =>
        o.JsonSerializerSettings.DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include);

    builder.Host.UseOrleans(siloBuilder =>
    {
        siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = llmResponseTimeout);
        siloBuilder.UseAdoNetClustering(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = connectionString;
        });

        siloBuilder.AddAdoNetGrainStorage("urls", options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = connectionString;
        });
        siloBuilder.AddAdoNetGrainStorage("personas", options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = connectionString;
        });

        siloBuilder.AddAdoNetGrainStorage("parties", options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = connectionString;
        });

        siloBuilder.AddAdoNetGrainStorage("imports", options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = connectionString;
        });

        siloBuilder.AddStateStorageBasedLogConsistencyProvider("PartyStateStorage");

        siloBuilder
            .AddMemoryStreams("party-streams")
            .AddMemoryGrainStorage("PubSubStore");

        siloBuilder.AddIncomingGrainCallFilter<GrainLoggingFilter>();

        siloBuilder.AddActivityPropagation();
    });

    // The co-hosted client (MemoryExtractor / StanceConsolidator call grains via
    // IGrainFactory) sets the request deadline, so it needs the same headroom.
    builder.Services.Configure<ClientMessagingOptions>(o => o.ResponseTimeout = llmResponseTimeout);

    builder.Services.AddHostedService<NarratorSeederHostedService>();
    builder.Services.AddHostedService<PartyTown.World.WorldPulseService>();
}

builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(Tracing.PersonaSourceName));

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseOutputCache();

app.Logger.LogInformation("App starting...");
app.Logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

app.UsePathBase("/api");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/api/openapi/v1.json");
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/api/openapi/v1.json", "v1");
    });
}

app.MapGet("/up", () => Results.NoContent()).Produces(StatusCodes.Status204NoContent);
app.UseWebSockets();

app.MapControllers();

app.Run();
