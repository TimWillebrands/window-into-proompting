using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = DistributedApplication.CreateBuilder(args);

var pgUser = builder.AddParameter("postgres-user", "partytown");
var pgPass = builder.AddParameter("postgres-password", secret: true);
var openRouterKey = builder.AddParameter("openrouter-api-key", secret: true);
var clerkSecretKey = builder.AddParameter("clerk-secret-key", secret: true);

var postgres = builder
    .AddPostgres("age-db", userName: pgUser, password: pgPass, port: 5455)
    .WithImage("apache/age")
    .WithImageTag("release_PG16_1.6.0")
    .WithDataVolume("partytown-pgdata")
    .WithBindMount("../../docker-entrypoint-initdb.d", "/docker-entrypoint-initdb.d", isReadOnly: true);

var partydb = postgres.AddDatabase("partydb", databaseName: "partytown");
partydb.WithPostgresMcp();

var backend = builder
    .AddProject<Projects.backend>("backend")
    .WithReference(partydb, connectionName: "Default")
    .WaitFor(partydb)
    .WithEnvironment("OPENROUTER_API_KEY", openRouterKey)
    .WithEnvironment("CLERK_SECRET_KEY", clerkSecretKey)
    .WithEndpoint("http", e => e.Port = 5072)
    .WithEndpoint(name: "orleans-silo", port: 11111, targetPort: 11111, scheme: "tcp", isProxied: false)
    .WithEndpoint(name: "orleans-gateway", port: 30000, targetPort: 30000, scheme: "tcp", isProxied: false);

// Optional dev knobs passed through from the shell (System One / model overrides, ADR 0019).
foreach (var name in new[] { "TYPESAFE_API_KEY", "TYPESAFE_MODEL", "OPENROUTER_MODEL",
             "SYSTEM_ONE_OPENROUTER", "SYSTEM_ONE_OPENROUTER_MODEL" })
{
    if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
        backend.WithEnvironment(name, value);
}

// Optional: production's Tailnet sidecar (ADR 0033), on when the shell has a key. Loopback only: SOCKS5 here has no auth.
if (Environment.GetEnvironmentVariable("TS_AUTHKEY") is { Length: > 0 } tailscaleKey)
{
    builder.AddContainer("tailscale", "tailscale/tailscale", "v1.104.4")
        .WithEnvironment("TS_AUTHKEY", tailscaleKey)
        .WithEnvironment("TS_HOSTNAME", "proompting-dev")
        .WithEnvironment("TS_STATE_DIR", "/var/lib/tailscale")
        .WithEnvironment("TS_AUTH_ONCE", "true")
        .WithEnvironment("TS_SOCKS5_SERVER", ":1055")
        .WithVolume("partytown-tailscale", "/var/lib/tailscale")
        .WithContainerRuntimeArgs("-p", "127.0.0.1:1055:1055");
    backend.WithEnvironment("Tailnets__home", "socks5://127.0.0.1:1055");
}

// Optional: the CDU (ADR 0039), on when user-secrets or the shell name its model upstream. Local Ollama works.
if (builder.Configuration["CDU_UPSTREAM_URL"] is { Length: > 0 } cduUpstream)
{
    var cduToken = builder.Configuration["CDU_TOKEN"] ?? "cdu-dev";
    var cduTab = builder.Configuration["CDU_METER_TAB"] ?? "cdu-dev-tab";
    var cduMeter = builder.AddContainer("cdu-meter", "node", "24-alpine")
        .WithBindMount("../../tools/cdu/src", "/app/src", isReadOnly: true)
        .WithBindMount(builder.Configuration["CDU_MODELS"] ?? "../../tools/cdu/models.json", "/app/models.json", isReadOnly: true)
        .WithVolume("partytown-cdu-meter", "/data")
        .WithArgs("node", "/app/src/meter-main.ts")
        .WithEnvironment("METER_UPSTREAM_URL", cduUpstream)
        .WithEnvironment("METER_UPSTREAM_KEY", builder.Configuration["CDU_UPSTREAM_KEY"] ?? string.Empty)
        .WithEnvironment("METER_SECRET", builder.Configuration["CDU_METER_SECRET"] ?? "cdu-dev-meter")
        .WithEnvironment("METER_TAB_TOKEN", cduTab)
        .WithEnvironment("METER_TAB_DAILY_USD", builder.Configuration["CDU_DAILY_USD"] ?? "0.5")
        .WithEnvironment("METER_STATE", "/data/tabs.json")
        .WithContainerRuntimeArgs("--add-host", "host.docker.internal:host-gateway");
    var cduDb = builder.AddContainer("cdu-db", "apache/age", "release_PG16_1.6.0")
        .WithEnvironment("POSTGRES_USER", "partytown")
        .WithEnvironment("POSTGRES_PASSWORD", "postgresPW")
        .WithEnvironment("POSTGRES_DB", "partytown")
        .WithBindMount("../../docker-entrypoint-initdb.d", "/docker-entrypoint-initdb.d", isReadOnly: true);
    var cdu = builder.AddDockerfile("cdu", "../..", "tools/cdu/Dockerfile")
        .WithVolume("partytown-cdu", "/cdu")
        .WithHttpEndpoint(port: 8790, targetPort: 8790, name: "http", isProxied: false)
        .WithEnvironment("CDU_TOKEN", cduToken)
        .WithEnvironment("CDU_METER_TAB", cduTab)
        .WithEnvironment("CDU_METER_URL", "http://cdu-meter:8788/v1")
        .WithEnvironment("CDU_DATABASE_URL", ReferenceExpression.Create($"postgresql://{pgUser}:{pgPass}@age-db:5432/partytown"))
        .WithEnvironment("PARTYTOWN_TEST_CONN", "Host=cdu-db;Port=5432;Database=partytown;Username=partytown;Password=postgresPW")
        .WithContainerRuntimeArgs("--cpus", "6", "--memory", "12g")
        .WaitFor(cduMeter)
        .WaitFor(cduDb)
        .WaitFor(postgres);
    foreach (var name in new[] { "CDU_MODEL", "CDU_ORIGIN", "CDU_GITHUB_REPO", "CDU_BASE", "CDU_THINKING" })
    {
        if (builder.Configuration[name] is { Length: > 0 } value) cdu.WithEnvironment(name, value);
    }
    if (builder.Configuration["CDU_GITHUB_TOKEN"] is { Length: > 0 } githubToken) cdu.WithEnvironment("GH_TOKEN", githubToken);
    backend.WithEnvironment("Cdu__Url", cdu.GetEndpoint("http")).WithEnvironment("Cdu__Token", cduToken);
}

var frontend = builder
    .AddViteApp("frontend", "../../frontend")
    .WithPnpm()
    .WithReference(backend)
    .WaitFor(backend)
    .WithEnvironment("VITE_API_URL", backend.GetEndpoint("http"))
    .WithEndpoint("http", e =>
    {
        e.Port = 8080;
        e.TargetPort = 8080;
        e.IsProxied = false;
    })
    .WithUrlForEndpoint("http", u => u.DisplayText = "Open Partytown");

// One switch for both sides, from user-secrets or the shell: the browser gets the token, its same-origin proxy the host.
if (builder.Configuration["POSTHOG_PROJECT_TOKEN"] is { Length: > 0 } posthogToken
    && builder.Configuration["POSTHOG_HOST"] is { Length: > 0 } posthogHost)
{
    backend.WithEnvironment("POSTHOG_PROJECT_TOKEN", posthogToken).WithEnvironment("POSTHOG_HOST", posthogHost);
    frontend.WithEnvironment("VITE_POSTHOG_PROJECT_TOKEN", posthogToken).WithEnvironment("POSTHOG_HOST", posthogHost);
}

frontend.OnResourceReady((resource, evt, ct) =>
{
    var url = frontend.GetEndpoint("http").Url;
    evt.Services.GetRequiredService<ILogger<Program>>()
        .LogInformation("🎉 Frontend ready: {Url}", url);
    return Task.CompletedTask;
});

builder.Build().Run();
