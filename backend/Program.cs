using Microsoft.Extensions.Options;
using PartyTown.Configuration;
using PartyTown.Logging;
using PartyTown.Services.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' not found.");

builder.Services.AddSingleton<IConfigureOptions<LlmOptions>>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new ConfigureOptions<LlmOptions>(options =>
    {
        var section = config.GetSection($"{LlmOptions.SectionName}:Providers");
        foreach (var child in section.GetChildren())
        {
            var type = child["Type"];
            ILlmProviderConfig provider = type switch
            {
                "ollama" => child.Get<OllamaProviderConfig>()!,
                "openrouter" => child.Get<OpenRouterProviderConfig>()!,
                _ => throw new InvalidOperationException($"Unknown LLM provider type: '{type}'")
            };
            options.Providers.Add(provider);
        }
    });
});

builder.Services.AddMemoryCache();
builder.Services.AddOutputCache();
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IPartyRealtimeHub, PartyRealtimeHub>();

builder.Host.UseOrleans(siloBuilder =>
{
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

    // Add grain call logging interceptor
    siloBuilder.AddIncomingGrainCallFilter<GrainLoggingFilter>();

    // Enable distributed tracing (ActivityPropagation)
    siloBuilder.AddActivityPropagation();
});

builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "HH:mm:ss ";
});

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
