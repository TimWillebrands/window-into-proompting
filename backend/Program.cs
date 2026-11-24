using PartyTown.Mcp;
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

var openApiBuild = Environment.GetEnvironmentVariable("OPENAPI_GENERATE") == "1";

builder.Services.AddLlmProviderOptions();

builder.Services.AddMemoryCache();
builder.Services.AddOutputCache();
builder.Services.AddOpenApi();
builder.Services.AddControllers(o =>
{
    o.Filters.Add<PartyTown.Services.Auth.ForbiddenActorFilter>();
    o.Filters.Add<PartyTown.Services.Auth.StoppedWorldFilter>();
});
builder.Services.AddClerkAuth(builder.Configuration);
builder.Services.AddPartyMcp();
builder.Services.AddHttpClient();
builder.Services.Configure<PartyTown.Services.Render.RenderOptions>(builder.Configuration.GetSection("Render"));
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient(PartyTown.Services.Render.PlaceRenderer.ClientName, c => c.Timeout = TimeSpan.FromSeconds(28)).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddSingleton<PartyTown.Services.Render.PlaceRenderer>();
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

    builder.Services.AddSingleton<PartyTown.World.IWorldLlm, PartyTown.World.WorldLlm>();
    builder.Services.AddSingleton<PartyTown.Services.SystemOne.LlmJudge>();
    builder.Services.AddSingleton<PartyTown.Services.SystemOne.ProviderJudge>();
    builder.Services.AddSingleton<PartyTown.Services.SystemOne.ISystemOneJudge>(sp =>
        sp.GetRequiredService<PartyTown.Services.SystemOne.ProviderJudge>());
#pragma warning disable EXTEXP0001
    builder.Services.AddHttpClient(PartyTown.Services.SystemOne.HttpSystemOneJudge.ClientName, c => c.Timeout = TimeSpan.FromSeconds(20)).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    builder.Services.AddSingleton<PartyTown.World.WorldMemory>();
    builder.Services.AddSingleton<PartyTown.World.Reflex.ReflexGate>();
    builder.Services.AddSingleton<PartyTown.World.PlayerRemoval>();
    builder.Services.AddSingleton<PartyTown.World.PlayerAuto>();
    builder.Services.AddSingleton<PartyTown.World.Notifications.PlayerInbox>();
    builder.Services.AddSingleton<PartyTown.World.WorldMove>();
    builder.Services.AddSingleton<PartyTown.World.Self.MomentAppraisal>();
    builder.Services.AddSingleton<PartyTown.World.Self.OffStage>();
    builder.Services.AddSingleton<PartyTown.World.Self.FollowThrough>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.MessengerApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.BoardApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.DiaryApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Apps.CalendarApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Places.PlacesApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Self.TogetherApp>();
    builder.Services.AddSingleton<PartyTown.World.Apps.IWorldApp, PartyTown.World.Relations.RelationsApp>();
    builder.Services.AddSingleton<PartyTown.World.Economy.WardrobeApp>();
    builder.Services.AddSingleton<PartyTown.World.Work.WorkDays>();
    builder.Services.AddSingleton<PartyTown.World.Work.Workplaces>();
    builder.Services.AddSingleton<PartyTown.World.Work.Responsibilities>();
    builder.Services.AddSingleton<PartyTown.World.Desires.DesireReview>();
    builder.Services.AddSingleton<PartyTown.World.Self.ThreadReview>();
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

    var llmResponseTimeout = TimeSpan.FromMinutes(3);

    builder.Services.Configure<Orleans.Serialization.OrleansJsonSerializerOptions>(o =>
        o.JsonSerializerSettings.DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include);

    builder.Host.UseOrleans(siloBuilder =>
    {
        siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = llmResponseTimeout);
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

    builder.Services.Configure<ClientMessagingOptions>(o => o.ResponseTimeout = llmResponseTimeout);

    builder.Services.AddHostedService<NarratorSeederHostedService>();
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

var noteLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PartyTown.SpeechCleanup");
PartyTown.Services.ResponsePipeline.SpeechCleanup.NoteDropped += (before, after) =>
    noteLog.LogInformation("Planning note cut: {Before} → {After}", PartyTown.World.WorldStream.Clip(before, 240), PartyTown.World.WorldStream.Clip(after, 120));

app.Logger.LogInformation("App starting...");
app.Logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

app.UseProxiedScheme();
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
app.UseOutputCache();

app.MapControllers();
app.MapPartyMcp();

if (!openApiBuild)
{
    await using var schema = new Npgsql.NpgsqlConnection(app.Configuration.GetConnectionString("Default"));
    await schema.OpenAsync();
    await MemoryGraphSchema.EnsureAsync(schema);
}

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
