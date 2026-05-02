var builder = DistributedApplication.CreateBuilder(args);

var pgUser = builder.AddParameter("postgres-user", "partytown");
var pgPass = builder.AddParameter("postgres-password", secret: true);
var openRouterKey = builder.AddParameter("openrouter-api-key", secret: true);

var postgres = builder
    .AddPostgres("age-db", userName: pgUser, password: pgPass, port: 5455)
    .WithImage("apache/age")
    .WithImageTag("release_PG16_1.6.0")
    .WithDataVolume("partytown-pgdata")
    .WithBindMount("../../docker-entrypoint-initdb.d", "/docker-entrypoint-initdb.d", isReadOnly: true);

var partydb = postgres.AddDatabase("partydb", databaseName: "partytown");

var backend = builder
    .AddProject<Projects.backend>("backend")
    .WithReference(partydb, connectionName: "Default")
    .WaitFor(partydb)
    .WithEnvironment("OPENROUTER_API_KEY", openRouterKey)
    .WithEndpoint("http", e => e.Port = 5072)
    .WithEndpoint(name: "orleans-silo", port: 11111, targetPort: 11111, scheme: "tcp", isProxied: false)
    .WithEndpoint(name: "orleans-gateway", port: 30000, targetPort: 30000, scheme: "tcp", isProxied: false);

builder
    .AddViteApp("frontend", "../../frontend")
    .WithNpm()
    .WithReference(backend)
    .WaitFor(backend)
    .WithEnvironment("VITE_API_URL", backend.GetEndpoint("http"))
    .WithEndpoint("http", e =>
    {
        e.Port = 8080;
        e.TargetPort = 8080;
        e.IsProxied = false;
    });

builder.Build().Run();
