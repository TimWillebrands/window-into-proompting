using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Orleans.Configuration;
using PartyTown.Configuration;
using PartyTown.Data;
using PartyTown.Logging;
using PartyTown.Observability;
using PartyTown.Services.Analytics;
using PartyTown.Services.Auth;
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
builder.Services.AddControllers(o => o.Filters.Add<PartyTown.Services.Auth.ForbiddenActorFilter>());
builder.Services.AddClerkAuth(builder.Configuration);
builder.Services.AddHttpClient();
builder.AddPostHogAnalytics();
builder.Services.AddSingleton<IAiObservabilityCapture, AiObservabilityCapture>();
builder.Services.AddSingleton<IPartyRealtimeHub, PartyRealtimeHub>();
builder.Services.AddSingleton<RaceTrigger>();
builder.Services.AddSingleton<ResponsePipeline>();

if (!openApiBuild)
{
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    if (string.IsNullOrEmpty(builder.Configuration["CLERK_SECRET_KEY"]))
        throw new InvalidOperationException("CLERK_SECRET_KEY not set.");

    builder.Services.AddSingleton<AgeOperatorInterceptor>();
    builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
        options
            .UseNpgsql(connectionString)
            .AddInterceptors(sp.GetRequiredService<AgeOperatorInterceptor>()));

    builder.Services.AddSingleton<IMemoryExtractor, MemoryExtractor>();
    builder.Services.AddSingleton<IMemoryRepository, MemoryRepository>();
            builder.Services.AddSingleton<IStanceConsolidator, StanceConsolidator>();
    builder.Services.AddSingleton<ConsolidationService>();

    // World (ADR 0018): Apps are modes of engagement; Life picks among their Affordances.
    builder.Services.AddSingleton<PartyTown.World.IWorldLlm, PartyTown.World.WorldLlm>();
    // System One (ADR 0034): the Decisions provider tier; with none answering, LLM emulation (ADR 0040).
    builder.Services.AddSingleton<PartyTown.Services.SystemOne.LlmJudge>();
    builder.Services.AddSingleton<PartyTown.Services.SystemOne.ProviderJudge>();
    builder.Services.AddSingleton<PartyTown.Services.SystemOne.ISystemOneJudge>(sp =>
        sp.GetRequiredService<PartyTown.Services.SystemOne.ProviderJudge>());
    // The router's bench is the resilience here: retrying a down local judge only adds latency and log noise.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is still marked experimental
    builder.Services.AddHttpClient(PartyTown.Services.SystemOne.HttpSystemOneJudge.ClientName, c => c.Timeout = TimeSpan.FromSeconds(20)).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    builder.Services.AddSingleton<PartyTown.World.WorldMemory>();
    builder.Services.AddSingleton<PartyTown.World.Reflex.ReflexGate>();
    builder.Services.AddSingleton<PartyTown.World.PlayerRemoval>();
    builder.Services.AddSingleton<PartyTown.World.PlayerAuto>();
    builder.Services.AddSingleton<PartyTown.World.Self.MomentAppraisal>();
    builder.Services.AddSingleton<PartyTown.World.Self.FollowThrough>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.RoomsApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.MessengerApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.BoardApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.DiaryApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.CalendarApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Places.PlacesApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Self.TogetherApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Relations.RelationsApp>();
    builder.Services.AddSingleton<PartyTown.World.Economy.WardrobeApp>();
    builder.Services.AddSingleton<PartyTown.World.Work.Workplaces>();
    builder.Services.AddSingleton<PartyTown.World.Work.Responsibilities>();
    builder.Services.AddSingleton<PartyTown.World.Desires.DesireReview>();
    builder.Services.AddSingleton<PartyTown.World.Desires.Mimesis>();
    builder.Services.AddSingleton<PartyTown.World.Relations.KeyWants>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Work.WorkApp>();
    builder.Services.AddHttpClient("markets", c => c.Timeout = TimeSpan.FromSeconds(10));
    builder.Services.AddSingleton<PartyTown.World.Work.Markets.IMarketFeed, PartyTown.World.Work.Markets.PolymarketFeed>();
    builder.Services.AddSingleton<PartyTown.World.Work.Markets.IMarketFeed, PartyTown.World.Work.Markets.KrakenFeed>();
    builder.Services.AddSingleton<PartyTown.World.Work.Markets.IMarketFeed, PartyTown.World.Work.Markets.YahooFeed>();
    builder.Services.AddSingleton<PartyTown.World.Work.Markets.MarketFeeds>();
    builder.Services.AddSingleton<PartyTown.World.Work.Companies.Desks>();
    builder.Services.AddSingleton<PartyTown.World.Work.Cdu.ICdu, PartyTown.World.Work.Cdu.HttpCdu>();
    builder.Services.AddSingleton<PartyTown.World.Work.Cdu.Cytheurgy>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Work.Cdu.CduApp>();
    builder.Services.AddSingleton<PartyTown.World.Work.Practices.Sessions>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Work.Companies.DeskApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Work.Practices.PracticeApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp>(sp => sp.GetRequiredService<PartyTown.World.Economy.WardrobeApp>());
    builder.Services.TryAddSingleton(TimeProvider.System);

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
        // Dev restarts kill the silo without a goodbye: declare the old instance dead fast instead of routing to it for minutes.
        if (builder.Environment.IsDevelopment())
            siloBuilder.Configure<ClusterMembershipOptions>(o =>
            {
                o.NumVotesForDeathDeclaration = 1;
                o.NumMissedProbesLimit = 1;
                o.ProbeTimeout = TimeSpan.FromSeconds(2);
            });
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
    // After the silo, before the World: the judge knows its providers before the first beat.
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PartyTown.Services.SystemOne.ProviderJudge>());
    builder.Services.AddHostedService<PartyTown.Grains.Generation.ProviderAvailabilityService>();
    builder.Services.AddHostedService<PartyTown.World.WorldPulseService>();
    builder.Services.AddHostedService<PartyTown.World.Work.Cdu.CduWatch>();
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

// The planning-note cut is a heuristic: log each firing to see whether the prompt fix made it redundant.
var noteLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PartyTown.SpeechCleanup");
PartyTown.Services.ResponsePipeline.SpeechCleanup.NoteDropped += (before, after) =>
    noteLog.LogInformation("Planning note cut: {Before} → {After}", PartyTown.World.WorldStream.Clip(before, 240), PartyTown.World.WorldStream.Clip(after, 120));

app.Logger.LogInformation("App starting...");
app.Logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

app.UsePathBase("/api");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/api/openapi/v1.json").AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/api/openapi/v1.json", "v1");
    });
}

app.MapGet("/up", () => Results.NoContent()).Produces(StatusCodes.Status204NoContent).AllowAnonymous();
app.UseWebSockets();

app.UseAuthentication();
app.UseAuthorization();
app.UsePostHogAnalytics();
// After auth: a cached response must not skip the sign-in check.
app.UseOutputCache();

app.MapControllers();

// Before the silo starts (app.Run): nothing may touch memory ahead of its schema (ADR 0008).
if (!openApiBuild)
{
    await using var schema = new Npgsql.NpgsqlConnection(app.Configuration.GetConnectionString("Default"));
    await schema.OpenAsync();
    await MemoryGraphSchema.EnsureAsync(schema);
}

// Dev runs one silo: a predecessor killed without a goodbye still reads Active and gets traffic until probed dead.
if (!openApiBuild && app.Environment.IsDevelopment())
{
    try
    {
        await using var db = new Npgsql.NpgsqlConnection(app.Configuration.GetConnectionString("Default"));
        await db.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("UPDATE orleansmembershiptable SET status = 6 WHERE status = 3", db);
        var stale = await cmd.ExecuteNonQueryAsync();
        if (stale > 0) app.Logger.LogInformation("Marked {Count} stale silo(s) dead before starting", stale);
    }
    catch (Exception ex)
    {
        app.Logger.LogDebug(ex, "Stale silo cleanup skipped");
    }
}

app.Run();
