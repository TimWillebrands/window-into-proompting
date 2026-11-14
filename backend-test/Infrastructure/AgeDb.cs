using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Images;
using Testcontainers.PostgreSql;

namespace BackendTest.Infrastructure;

/// <summary>One throwaway Postgres+AGE per test run, from an image with the init scripts already applied: start is a plain boot.</summary>
public static class AgeDb
{
    private const string BaseImage = "apache/age:release_PG16_1.6.0";
    private const string User = "partytown";
    private const string Password = "postgresPW";
    private const string Database = "partytown";

    // initdb and docker-entrypoint-initdb.d run at build time, into a PGDATA outside the image's VOLUME so it's kept.
    private static readonly string Dockerfile = $$"""
        FROM {{BaseImage}}
        ENV POSTGRES_USER={{User}} POSTGRES_PASSWORD={{Password}} POSTGRES_DB={{Database}} PGDATA=/pgdata
        COPY *.sql /docker-entrypoint-initdb.d/
        RUN mkdir -p /pgdata && chown postgres:postgres /pgdata
        USER postgres
        RUN bash -c 'set -e; source docker-entrypoint.sh; docker_setup_env; docker_create_db_directories; docker_verify_minimum_env; \
            docker_init_database_dir; pg_setup_hba_conf; export PGPASSWORD="$POSTGRES_PASSWORD"; \
            docker_temp_server_start; docker_setup_db; docker_process_init_files /docker-entrypoint-initdb.d/*; docker_temp_server_stop'
        """;

    private static readonly Lazy<Task<string>> Started = new(StartAsync);

    /// <summary>Connection string of the shared container; started (and its image built, if the scripts changed) on first use.</summary>
    public static Task<string> ConnectionStringAsync() => Started.Value;

    private static async Task<string> StartAsync()
    {
        var scripts = Directory.GetFiles(Path.Combine(CommonDirectoryPath.GetGitDirectory().DirectoryPath, "docker-entrypoint-initdb.d"), "*.sql")
            .Order(StringComparer.Ordinal).ToList();
        var context = Directory.CreateTempSubdirectory("age-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(context.FullName, "Dockerfile"), Dockerfile);
            foreach (var script in scripts) File.Copy(script, Path.Combine(context.FullName, Path.GetFileName(script)));
            var image = new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(context.FullName)
                .WithName($"partytown/age-test:{Hash(scripts)}")
                .WithImageBuildPolicy(PullPolicy.Missing)
                .WithCleanUp(false)
                .WithDeleteIfExists(false)
                .Build();
            await image.CreateAsync();

            var container = new PostgreSqlBuilder(image)
                .WithUsername(User).WithPassword(Password).WithDatabase(Database)
                .WithCommand("-c", "shared_preload_libraries=age", "-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off")
                .Build();
            await container.StartAsync();
            return container.GetConnectionString();
        }
        finally
        {
            context.Delete(recursive: true);
        }
    }

    /// <summary>Image tag from the Dockerfile and every script: a changed script builds a fresh image, else the cached one starts.</summary>
    private static string Hash(IEnumerable<string> scripts)
    {
        var all = new StringBuilder(Dockerfile);
        foreach (var script in scripts) all.Append(Path.GetFileName(script)).Append(File.ReadAllText(script));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString())))[..16];
    }
}
