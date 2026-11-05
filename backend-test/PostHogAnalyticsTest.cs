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
using PartyTown.Grains.Generation;
using PartyTown.Observability;
using PartyTown.Services.Analytics;
using PartyTown.Services.Auth;
using PostHog;

namespace BackendTest;

/// <summary>Real SDK against a local stand-in for PostHog, so nothing reaches the real project.</summary>
public sealed class PostHogAnalyticsTest
{
    private const string Session = "01a18aaf-b6f4-77e8-a648-6f9ab82865d0";

    [Fact]
    public async Task Unhandled_exception_is_captured_under_the_clerk_id()
    {
        await using var ingest = await FakeIngest.StartAsync();
        await using var app = await StartAppAsync(ingest.Url, "phc_test", userId: "user_1");

        var response = await Send(app, "/boom", consent: "1");
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

        await Send(app, "/boom", consent: "1");
        await app.Services.GetRequiredService<IPostHogClient>().FlushAsync();

        var captured = Assert.Single(ingest.Events);
        Assert.False(captured.GetProperty("properties").GetProperty("$process_person_profile").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public async Task Without_consent_actions_still_count_but_lead_to_nobody(string? consent)
    {
        await using var ingest = await FakeIngest.StartAsync();
        await using var app = await StartAppAsync(ingest.Url, "phc_test", userId: "user_1");

        await Send(app, "/track", consent, session: Session);
        await Send(app, "/track", consent, session: Session);
        await app.Services.GetRequiredService<IPostHogClient>().FlushAsync();

        Assert.Equal(2, ingest.Events.Count);
        Assert.Equal(2, ingest.Events.Select(e => e.GetProperty("distinct_id").GetString()).Distinct().Count());
        foreach (var captured in ingest.Events)
        {
            Assert.Equal("message_sent", captured.GetProperty("event").GetString());
            Assert.NotEqual("user_1", captured.GetProperty("distinct_id").GetString());
            var properties = captured.GetProperty("properties");
            Assert.False(properties.GetProperty("$process_person_profile").GetBoolean());
            Assert.True(properties.GetProperty("anonymous").GetBoolean());
            Assert.Equal("world", properties.GetProperty("party_id").GetString());
            Assert.Equal(5, properties.GetProperty("length").GetInt32());
            Assert.False(properties.TryGetProperty("room_id", out _));
            Assert.False(properties.TryGetProperty("$session_id", out _));
            Assert.False(properties.TryGetProperty("$ip", out _));
        }
    }

    [Fact]
    public async Task Consented_action_keeps_its_ids_and_is_not_marked_anonymous()
    {
        await using var ingest = await FakeIngest.StartAsync();
        await using var app = await StartAppAsync(ingest.Url, "phc_test", userId: "user_1");

        await Send(app, "/track", consent: "1");
        await app.Services.GetRequiredService<IPostHogClient>().FlushAsync();

        var properties = Assert.Single(ingest.Events).GetProperty("properties");
        Assert.Equal("room", properties.GetProperty("room_id").GetString());
        Assert.False(properties.TryGetProperty("anonymous", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public async Task Without_consent_exceptions_stay_personless_and_hide_ids_in_the_path(string? consent)
    {
        await using var ingest = await FakeIngest.StartAsync();
        await using var app = await StartAppAsync(ingest.Url, "phc_test", userId: "user_1");

        await Send(app, "/threads/secret-thread/boom", consent, session: Session);
        await app.Services.GetRequiredService<IPostHogClient>().FlushAsync();

        var captured = Assert.Single(ingest.Events);
        Assert.Equal("$exception", captured.GetProperty("event").GetString());
        Assert.NotEqual("user_1", captured.GetProperty("distinct_id").GetString());
        var properties = captured.GetProperty("properties");
        Assert.False(properties.GetProperty("$process_person_profile").GetBoolean());
        Assert.False(properties.TryGetProperty("$session_id", out _));
        Assert.Equal("/threads/{id}/boom", properties.GetProperty("path").GetString());
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
        Assert.Equal("production", properties.GetProperty("environment").GetString());
    }

    [Theory]
    [InlineData("01a18aaf-b6f4-77e8-a648-6f9ab82865d0", true)]
    [InlineData("not-a-session", false)]
    public async Task Tracked_action_joins_the_browser_session(string header, bool linked)
    {
        await using var ingest = await FakeIngest.StartAsync();
        await using var app = await StartAppAsync(ingest.Url, "phc_test", userId: "user_1");

        await Send(app, "/track", consent: "1", session: header);
        await app.Services.GetRequiredService<IPostHogClient>().FlushAsync();

        var captured = Assert.Single(ingest.Events);
        Assert.Equal("user_1", captured.GetProperty("distinct_id").GetString());
        var properties = captured.GetProperty("properties");
        Assert.Equal(linked, properties.TryGetProperty("$session_id", out var session));
        if (linked) Assert.Equal(header, session.GetString());
    }

    [Fact]
    public async Task Ai_generation_is_personless_and_never_throws_when_off()
    {
        await using var ingest = await FakeIngest.StartAsync();
        await using var app = await StartAppAsync(ingest.Url, "phc_test", userId: null);
        await using var off = await StartAppAsync("", "", userId: null);
        var messages = new[] { new LlmChatMessage { Role = "user", Content = "hi" } };

        await new AiObservabilityCapture(off.Services.GetRequiredService<IPostHogClient>())
            .CaptureGenerationAsync(messages, "hello", "openrouter", "m", "http://llm", 0.5, streamed: true, isError: false);
        var posthog = app.Services.GetRequiredService<IPostHogClient>();
        await new AiObservabilityCapture(posthog)
            .CaptureGenerationAsync(messages, "hello", "openrouter", "m", "http://llm", 0.5, streamed: true, isError: false);
        await posthog.FlushAsync();

        var captured = Assert.Single(ingest.Events);
        Assert.Equal("$ai_generation", captured.GetProperty("event").GetString());
        var properties = captured.GetProperty("properties");
        Assert.False(properties.GetProperty("$process_person_profile").GetBoolean());
        Assert.Equal("hi", properties.GetProperty("$ai_input")[0].GetProperty("content").GetString());
        Assert.Equal("hello", properties.GetProperty("$ai_output_choices")[0].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("", "http://localhost:1")]
    [InlineData("phc_test", "")]
    public async Task Missing_config_keeps_it_off(string token, string host)
    {
        await using var app = await StartAppAsync(host, token, userId: null);

        Assert.False(app.Services.GetRequiredService<IPostHogClient>().Capture("user_1", "message_sent"));
    }

    private static async Task<HttpResponseMessage> Send(WebApplication app, string path, string? consent, string? session = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url(app)}{path}");
        request.Headers.Add("X-POSTHOG-DISTINCT-ID", "someone_else");
        if (consent is not null) request.Headers.Add("Cookie", $"other=x; analytics_consent={consent}");
        if (session is not null) request.Headers.Add("X-POSTHOG-SESSION-ID", session);
        return await new HttpClient().SendAsync(request);
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
        app.MapGet("/track", (HttpContext context, IPostHogClient posthog) =>
            posthog.Track(context, "message_sent", new Dictionary<string, object>
            {
                ["party_id"] = "world", ["room_id"] = "room", ["length"] = 5,
            }));
        app.MapGet("/threads/{id}/boom", (HttpContext _) => { throw new InvalidOperationException("boom"); });
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
