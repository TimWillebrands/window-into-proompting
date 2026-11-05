using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PartyTown.Services.Analytics;
using PartyTown.Services.Auth;
using PostHog;

namespace BackendTest;

/// <summary>Real SDK against a local stand-in for PostHog, so nothing reaches the real project.</summary>
public sealed class PostHogAnalyticsTest
{
    [Fact]
    public async Task Unhandled_exception_is_captured_under_the_clerk_id()
    {
        await using var ingest = await FakeIngest.StartAsync();
        await using var app = await StartAppAsync(ingest.Url, "phc_test", userId: "user_1");

        var response = await new HttpClient().GetAsync($"{Url(app)}/boom");
        await app.Services.GetRequiredService<IPostHogClient>().FlushAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var captured = Assert.Single(ingest.Events);
        Assert.Equal("$exception", captured.GetProperty("event").GetString());
        Assert.Equal("user_1", captured.GetProperty("distinct_id").GetString());
        Assert.Equal("/boom", captured.GetProperty("properties").GetProperty("path").GetString());
    }

    [Fact]
    public async Task Anonymous_exception_is_captured_without_a_person()
    {
        await using var ingest = await FakeIngest.StartAsync();
        await using var app = await StartAppAsync(ingest.Url, "phc_test", userId: null);

        await new HttpClient().GetAsync($"{Url(app)}/boom");
        await app.Services.GetRequiredService<IPostHogClient>().FlushAsync();

        var captured = Assert.Single(ingest.Events);
        Assert.False(captured.GetProperty("properties").GetProperty("$process_person_profile").GetBoolean());
    }

    [Fact]
    public async Task Events_carry_guids_and_the_environment()
    {
        await using var ingest = await FakeIngest.StartAsync();
        await using var app = await StartAppAsync(ingest.Url, "phc_test", userId: "user_1");
        var party = Guid.NewGuid();

        var posthog = app.Services.GetRequiredService<IPostHogClient>();
        posthog.Capture("user_1", "message_sent", new Dictionary<string, object> { ["party_id"] = party });
        await posthog.FlushAsync();

        var properties = Assert.Single(ingest.Events).GetProperty("properties");
        Assert.Equal(party.ToString(), properties.GetProperty("party_id").GetString());
        Assert.Equal("Production", properties.GetProperty("environment").GetString());
    }

    [Theory]
    [InlineData("", "http://localhost:1")]
    [InlineData("phc_test", "")]
    public async Task Missing_config_keeps_it_off(string token, string host)
    {
        await using var app = await StartAppAsync(host, token, userId: null);

        Assert.False(app.Services.GetRequiredService<IPostHogClient>().Capture("user_1", "message_sent"));
    }

    private static string Url(WebApplication app)
        => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

    private static async Task<WebApplication> StartAppAsync(string host, string token, string? userId)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["POSTHOG_PROJECT_TOKEN"] = token;
        builder.Configuration["POSTHOG_HOST"] = host;
        builder.AddPostHogAnalytics();

        var app = builder.Build();
        app.Use((context, next) =>
        {
            if (userId is not null)
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClerkAuth.UserIdClaim, userId) }, "test"));
            return next(context);
        });
        app.UsePostHogAnalytics();
        app.MapGet("/boom", (HttpContext _) => { throw new InvalidOperationException("boom"); });
        await app.StartAsync();
        return app;
    }

    /// <summary>Records every event POSTed to /batch, gzipped or not.</summary>
    private sealed class FakeIngest : IAsyncDisposable
    {
        private readonly WebApplication app;
        public List<JsonElement> Events { get; } = new();
        public string Url => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        private FakeIngest()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            app = builder.Build();
            app.MapPost("/batch", async (HttpContext context) =>
            {
                var gzipped = context.Request.Headers.ContentEncoding.ToString().Contains("gzip");
                await using Stream body = gzipped ? new GZipStream(context.Request.Body, CompressionMode.Decompress) : context.Request.Body;
                using var doc = await JsonDocument.ParseAsync(body);
                lock (Events) Events.AddRange(doc.RootElement.GetProperty("batch").EnumerateArray().Select(e => e.Clone()));
                return Results.Ok(new { status = 1 });
            });
        }

        public static async Task<FakeIngest> StartAsync()
        {
            var ingest = new FakeIngest();
            await ingest.app.StartAsync();
            return ingest;
        }

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }
}
