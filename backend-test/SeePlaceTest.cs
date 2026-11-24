using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Moq;
using PartyTown.Mcp;
using PartyTown.Services.Auth;
using PartyTown.Services.Render;
using PartyTown.World.Places;

namespace BackendTest;

public partial class WorldLifeTest
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private sealed class Camera : HttpMessageHandler
    {
        public JsonObject? Asked { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct)) as JsonObject;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) { Headers = { ContentType = new("image/png") } } };
        }
    }

    private async Task<(Guid PartyId, Guid Player, PlayerTools Tools, Camera Camera, PlaceRenderer Renderer)> SeeingWorldAsync()
    {
        var (partyId, player, _, _) = await PlayerWorldAsync();
        var camera = new Camera();
        var renderer = new PlaceRenderer(
            Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient(camera)),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new RenderOptions { BaseUrl = "http://render.test", ScreenshotUrl = "http://camera.test/screenshot" }),
            NullLogger<PlaceRenderer>.Instance);
        var tools = new PlayerTools(_fx.GrainFactory, new Actors(_fx.GrainFactory), Options.Create(new McpOptions { World = partyId }), renderer: renderer);
        return (partyId, player, tools, camera, renderer);
    }

    [Fact]
    public async Task AnAgent_SeesItsHome()
    {
        var (partyId, player, tools, camera, _) = await SeeingWorldAsync();
        var home = (await PlacesOf(partyId).GetSnapshotAsync()).Places.HomeOf(player)!;

        var seen = (await tools.SeePlaceAsync(Agent(), hour: 30)).ToList();

        Assert.Equal(Png, Assert.Single(seen.OfType<ImageContentBlock>()).DecodedData.ToArray());
        Assert.Equal($"http://render.test/render/place/{home.Id}?world={partyId}&hour=23", camera.Asked?["url"]?.ToString());
    }

    [Fact]
    public async Task AnAgent_SeesADesignBeforeItIsApplied()
    {
        var (partyId, player, tools, camera, renderer) = await SeeingWorldAsync();
        var before = (await PlacesOf(partyId).GetSnapshotAsync()).Places.HomeOf(player)!;

        var seen = (await tools.SeePlaceAsync(Agent(), design: Loft())).ToList();

        var url = new Uri(camera.Asked!["url"]!.ToString());
        var key = System.Web.HttpUtility.ParseQueryString(url.Query)["draft"]!;
        var drawn = renderer.Draft(key)!;
        var after = (await PlacesOf(partyId).GetSnapshotAsync()).Places.HomeOf(player)!;
        Assert.Contains("not applied", Assert.Single(seen.OfType<TextContentBlock>()).Text);
        Assert.Equal(before.Id, drawn.Id);
        Assert.NotEqual(before.CustomScene, drawn.CustomScene);
        Assert.Contains("sofa", drawn.Spots.Select(s => s.Id));
        Assert.Equal(before.CustomScene, after.CustomScene);
    }

    [Fact]
    public async Task AnAgent_IsToldWhenADesignCannotBeSeen()
    {
        var (_, _, tools, camera, _) = await SeeingWorldAsync();

        var wrong = await Assert.ThrowsAsync<McpException>(() => tools.SeePlaceAsync(Agent(), design: Loft(door: "#")));

        Assert.Contains("doesn't work yet", wrong.Message);
        Assert.Null(camera.Asked);
    }
}
