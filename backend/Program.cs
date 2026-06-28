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
builder.Services.AddSingleton<ImportService>();
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
    builder.Services.AddSingleton<IStanceConsolidator, StanceConsolidator>();
    builder.Services.AddSingleton<ConsolidationService>();

    // LLM one-shot calls (memory extraction, stance consolidation) routinely run
    // longer than Orleans' default 30s response timeout, which would otherwise drop
    // the in-flight response as expired. Give the grain<->client RPC generous headroom.
    var llmResponseTimeout = TimeSpan.FromMinutes(3);

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
app.Logger.LogInformation("LLM Provider: {Provider}", builder.Configuration["Llm:Provider"]);
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
