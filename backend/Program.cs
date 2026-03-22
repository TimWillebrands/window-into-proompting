using PartyTown;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.EventSourcing;
using PartyTown.Configuration;
using PartyTown.Logging;
using PartyTown.Services.Llm;
using PartyTown.Services.Realtime;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' not found.");

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<OpenRouterModelsClient>();
builder.Services.AddSingleton<ILlmProvider, OllamaLlmProvider>();
builder.Services.AddSingleton<ILlmProvider, OpenRouterLlmProvider>();
builder.Services.AddSingleton<ILlmProviderRegistry, LlmProviderRegistry>();
builder.Services.AddSingleton<IPartyRealtimeHub, PartyRealtimeHub>();

builder.Host.UseOrleans(siloBuilder =>
{
    if (builder.Environment.IsDevelopment())
    {
        siloBuilder.UseLocalhostClustering();
    }
    else
    {
        siloBuilder.UseAdoNetClustering(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = connectionString;
        });
    }

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

    // Add grain call logging interceptor
    siloBuilder.AddIncomingGrainCallFilter<GrainLoggingFilter>();

    // Enable distributed tracing (ActivityPropagation)
    siloBuilder.AddActivityPropagation();
});

// Configure console logging with scopes
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "HH:mm:ss ";
});

var app = builder.Build();

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

app.UseHttpsRedirection();
app.UseWebSockets();

app.MapControllers();

app.Run();
