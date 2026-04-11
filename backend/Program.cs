using Microsoft.Extensions.Options;
using PartyTown.Configuration;
using PartyTown.Logging;
using PartyTown.Services.Realtime;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

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

// Configure console logging with scopes and OpenTelemetry
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "HH:mm:ss ";
})
.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

var otel = builder.Services.AddOpenTelemetry();

// Add Metrics for ASP.NET Core and our custom metrics and export via OTLP
otel.WithMetrics(metrics =>
{
    // Metrics provider from OpenTelemetry
    metrics.AddAspNetCoreInstrumentation();
    // Metrics provides by ASP.NET Core in .NET 8
    metrics.AddMeter("Microsoft.AspNetCore.Hosting");
    metrics.AddMeter("Microsoft.AspNetCore.Server.Kestrel");
});

// Add Tracing for ASP.NET Core and our custom ActivitySource and export via OTLP
otel.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation();
    tracing.AddHttpClientInstrumentation();
});

// Export OpenTelemetry data via OTLP, using env vars for the configuration
var OtlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (OtlpEndpoint != null)
{
    otel.UseOtlpExporter();
}

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

app.MapGet("/up", () => Results.Ok());
app.UseWebSockets();

app.MapControllers();

app.Run();
