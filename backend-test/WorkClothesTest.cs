using System.Text.Json;
using System.Text.Json.Nodes;
using BackendTest.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.Hosting;
using Orleans.TestingHost;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Economy;
using PartyTown.World.Places;
using PartyTown.World.Work;

namespace BackendTest;

public class WorkClothesTest : IClassFixture<WorkClothesTest.Fixture>
{
    private readonly Fixture _fx;

    public WorkClothesTest(Fixture fx) => _fx = fx;

    private static readonly DateTimeOffset Monday9 = new(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(1));

    private const string EikoOwn = """
        {"picks": {"hair": "black", "hairStyle": "bun", "topStyle": "band-tee", "top": "purple", "bottomStyle": "skirt", "bottom": "white", "shoeStyle": "sneakers", "shoes": "grey"},
         "summary": "Black hair in a bun, a purple band tee and a white skirt, a sketchbook under one arm.",
         "design": {"face": {"shape": 0.45, "eyes": {"shape": "almond", "iris": "#2a1d17"}},
          "hair": {"color": "#141016", "cap": 1, "zones": [{"at": "crown", "count": 14, "len": 0.35}]},
          "shoes": {"kind": "sneaker", "color": "#cfd1d6"},
          "extras": [{"name": "sketchbook", "socket": "hip-l", "blobs": [{"shape": "capsule", "at": [0.1, 0.2, 0.05], "r": 0.1, "color": "#8a6d4b", "to": [0.1, -0.5, 0.05]}]}]}}
        """;

    private const string ShrineDress = """
        {"code": "dress", "summary": "In a white kosode and a vermilion hakama, white tabi and straw sandals.",
         "picks": {"topStyle": "shirt", "top": "white", "bottomStyle": "trousers", "bottom": "red", "shoeStyle": "sandals", "shoes": "white", "hair": "pink", "skin": "dark"},
         "design": {"top": {"color": "#f7f5ef", "sleeve": 1, "hem": 0.6, "fit": 0.6, "neck": "v"},
          "bottom": {"color": "#b3262a", "kind": "pants", "length": 1, "fit": 0.9, "flare": 1, "rise": 1, "pleats": 0.8},
          "shoes": {"kind": "sandal", "color": "#f4f1ea"},
          "extras": [{"name": "gohei", "socket": "hand-r", "blobs": [{"shape": "capsule", "at": [0, 0, 0], "to": [0, 0.6, 0], "r": 0.02, "color": "#c9a77a"}]}],
          "face": {"shape": 0.9}, "hair": {"color": "#ff00ff", "cap": 1, "zones": []}, "body": {"height": 1}, "stance": {"spine": [20, 0, 0]}}}
        """;

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static string Json(Appearance a) => JsonSerializer.Serialize(a);

    [Fact]
    public void Clean_KeepsOnlyGarments()
    {
        var clothes = WorkClothesRules.Clean(Obj(ShrineDress), "priestess");

        Assert.False(clothes.OwnClothes);
        Assert.Equal("priestess", clothes.For);
        Assert.Equal("red", clothes.Picks["bottom"]);
        Assert.False(clothes.Picks.ContainsKey("hair"));
        Assert.False(clothes.Picks.ContainsKey("skin"));
        var design = Obj(clothes.Design!);
        Assert.NotNull(design["bottom"]);
        Assert.NotNull(design["extras"]);
        Assert.Null(design["face"]);
        Assert.Null(design["hair"]);
        Assert.Null(design["body"]);
        Assert.Null(design["stance"]);
    }

    [Theory]
    [InlineData("""{"code": "none", "picks": {"topStyle": "suit"}}""")]
    [InlineData("""{"code": "dress", "picks": {"hair": "pink"}}""")]
    public void Clean_NothingToWear_IsTheirOwnClothes(string answer)
        => Assert.True(WorkClothesRules.Clean(Obj(answer), "writer").OwnClothes);

    [Fact]
    public void Wear_LaysGarmentsOver_KeepsWhoTheyAre()
    {
        var own = Eiko();
        var worn = WorkClothesRules.Wear(own, WorkClothesRules.Clean(Obj(ShrineDress), "priestess"));

        Assert.Equal("red", worn.Bottom);
        Assert.Equal("sandals", worn.ShoeStyle);
        Assert.Equal(own.Hair, worn.Hair);
        Assert.Equal(own.Skin, worn.Skin);
        var design = Obj(worn.Design!);
        Assert.Equal(Obj(own.Design!)["face"]!.ToJsonString(), design["face"]!.ToJsonString());
        Assert.Equal(Obj(own.Design!)["hair"]!.ToJsonString(), design["hair"]!.ToJsonString());
        Assert.Equal("sandal", design["shoes"]!["kind"]!.ToString());
        Assert.Equal(new[] { "sketchbook", "gohei" }, design["extras"]!.AsArray().Select(e => e!["name"]!.ToString()));
        Assert.Contains("hakama", worn.Summary);
        Assert.Equal("purple", own.Top);
    }

    [Fact]
    public void SameTitle_KeepsTheClothes_ANewOneDropsThem()
    {
        var clothes = new WorkClothes { For = "priestess", Made = WorkClothesRules.Made };
        var kept = new Occupation { Title = "Priestess" };
        WorkRules.CarryClothes(kept, new Occupation { Title = "priestess", Clothes = clothes });
        Assert.Same(clothes, kept.Clothes);

        var changed = new Occupation { Title = "barista" };
        WorkRules.CarryClothes(changed, new Occupation { Title = "priestess", Clothes = clothes });
        Assert.Null(changed.Clothes);
    }

    [Fact]
    public void ClothesMadeBeforeAFix_AreMadeAgain()
    {
        var stale = new WorkClothes { For = "priestess", OwnClothes = true, Made = WorkClothesRules.Made - 1 };
        Assert.False(WorkClothesRules.Fits(stale, "priestess"));
        var job = new Occupation { Title = "priestess" };
        WorkRules.CarryClothes(job, new Occupation { Title = "priestess", Clothes = stale });
        Assert.Null(job.Clothes);
    }

    [Fact]
    public async Task Eiko_GoesToWorkInShrineDress_AndComesBackAsSheWas()
    {
        var w = await _fx.EikoAtWorkAsync();
        var before = await w.LookAsync();
        Assert.Equal("band-tee", before.TopStyle);

        await w.Apply(await w.Occupation.StepAsync(Monday9, false));
        var body = await w.BodyAsync();
        Assert.NotNull(body.WorkClothes);
        Assert.Equal("red", body.Appearance!.Bottom);
        Assert.Equal("sandals", body.Appearance.ShoeStyle);
        Assert.Contains("gohei", body.Appearance.Design);
        Assert.Contains("sketchbook", body.Appearance.Design);
        Assert.Equal(Json(before), Json(body.OwnLook!));

        await w.Apply(await w.Occupation.StepAsync(Monday9.AddHours(8), false));
        body = await w.BodyAsync();
        Assert.Null(body.WorkClothes);
        Assert.Null(body.OwnLook);
        Assert.Equal(Json(before), Json(body.Appearance!));
    }

    [Fact]
    public async Task LeavingEarly_ChangesBack()
    {
        var w = await _fx.EikoAtWorkAsync();
        var before = await w.LookAsync();
        await w.Apply(await w.Occupation.StepAsync(Monday9, false));

        var record = await w.Occupation.LeaveEarlyAsync(Monday9.AddHours(2));
        await w.Workplaces.LeaveEarlyAsync(w.PartyId, w.Eiko, (await w.Occupation.GetAsync()).Job!, record!);

        Assert.Equal(Json(before), Json(await w.LookAsync()));
    }

    [Fact]
    public async Task LosingTheJobMidShift_ChangesBack()
    {
        var w = await _fx.EikoAtWorkAsync();
        var before = await w.LookAsync();
        await w.Apply(await w.Occupation.StepAsync(Monday9, false));

        await w.Occupation.SetAsync(null);

        Assert.Equal(Json(before), Json(await w.LookAsync()));
    }

    [Fact]
    public async Task NoDressCode_ChangesNothing()
    {
        var w = await _fx.EikoAtWorkAsync(new WorkClothes { OwnClothes = true });
        var before = await w.LookAsync();

        await w.Apply(await w.Occupation.StepAsync(Monday9, false));

        var body = await w.BodyAsync();
        Assert.Null(body.WorkClothes);
        Assert.Null(body.OwnLook);
        Assert.Equal(Json(before), Json(body.Appearance!));
    }

    [Fact]
    public async Task BuyingOnShift_IsWornAfterWork()
    {
        var w = await _fx.EikoAtWorkAsync();
        await w.Apply(await w.Occupation.StepAsync(Monday9, false));

        var wardrobe = new WardrobeApp(_fx.GrainFactory, Mock.Of<ISystemOneJudge>(), NullLogger<WardrobeApp>.Instance);
        var bought = await wardrobe.BuyAsync(w.PartyId, w.Eiko, Catalog.Find("top:hoodie:green")!);
        Assert.Contains("after work", bought!.Summary);
        var body = await w.BodyAsync();
        Assert.Equal("shirt", body.Appearance!.TopStyle);
        Assert.Equal("hoodie", body.OwnLook!.TopStyle);

        await w.Apply(await w.Occupation.StepAsync(Monday9.AddHours(8), false));
        Assert.Equal("hoodie", (await w.LookAsync()).TopStyle);
    }

    [Fact]
    public async Task NotDesignedYet_TheyChangeOnceTheyAre()
    {
        var w = await _fx.EikoAtWorkAsync(designed: false);
        await w.Apply(await w.Occupation.StepAsync(Monday9, false));

        var clothes = await Eventually(async () => (await w.Occupation.GetAsync()).Job?.Clothes);
        Assert.False(clothes.OwnClothes);
        Assert.Contains("hakama", clothes.Summary);
        var body = await Eventually(async () => await w.BodyAsync() is { WorkClothes: not null } b ? b : null);
        Assert.Equal("red", body.Appearance!.Bottom);
    }

    [Fact]
    public async Task APlayer_IsNeverDressedUnasked()
    {
        var w = await _fx.EikoAtWorkAsync(designed: false, human: true);
        await w.Apply(await w.Occupation.StepAsync(Monday9, false));
        await Task.Delay(500);

        Assert.Null((await w.Occupation.GetAsync()).Job!.Clothes);
        Assert.Null((await w.BodyAsync()).WorkClothes);
    }

    private static async Task<T> Eventually<T>(Func<Task<T?>> read) where T : class
    {
        for (var i = 0; i < 50; i++)
        {
            if (await read() is { } v) return v;
            await Task.Delay(100);
        }
        throw new TimeoutException("Never happened.");
    }

    private static Appearance Eiko()
    {
        var answer = Obj(EikoOwn);
        var look = AppearanceRules.Merge(new Appearance(), answer["picks"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value?.ToString()));
        look.Design = FigureDesign.Clean(answer["design"]);
        look.Summary = answer["summary"]!.ToString();
        return look;
    }

    public sealed record World(Fixture Fx, Guid PartyId, Guid Eiko, CastMember Self, IOccupationGrain Occupation, Workplaces Workplaces)
    {
        public IPlacesGrain Places => Fx.GrainFactory.GetGrain<IPlacesGrain>(PartyId);
        public async Task<Body> BodyAsync() => (await Places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == Eiko);
        public async Task<Appearance> LookAsync() => (await BodyAsync()).Appearance!;
        public Task Apply(ShiftChange change) => Workplaces.ApplyAsync(PartyId, Self, change);
    }

    private sealed class Tailor : IWorldLlm
    {
        public Task<string?> CompleteAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null)
            => Task.FromResult<string?>(null);

        public Task<JsonObject?> CompleteJsonAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null,
            IReadOnlyList<string>? fields = null, IReadOnlyList<string>? flags = null, Func<JsonObject, string?>? refuse = null)
            => Task.FromResult(system == FigureDesign.WorkPrompt && user.Contains("priestess")
                ? fields is null ? Obj(ShrineDress) : new JsonObject { ["code"] = "dress" }
                : null);
    }

    public sealed class Fixture : IAsyncLifetime
    {
        public TestCluster Cluster { get; private set; } = null!;
        public IGrainFactory GrainFactory => Cluster.GrainFactory;

        public Task InitializeAsync()
        {
            var builder = new TestClusterBuilder(1);
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            Cluster = builder.Build();
            return Cluster.DeployAsync();
        }

        public Task DisposeAsync() => Cluster.DisposeAsync().AsTask();

        public async Task<World> EikoAtWorkAsync(WorkClothes? clothes = null, bool human = false, bool designed = true)
        {
            var partyId = Guid.NewGuid();
            var eiko = Guid.NewGuid();
            await GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty)
                .AddPersona(new Persona(eiko, "Eiko Tanaka", "You are Eiko.", "The priestess of a small Shinto shrine."));
            var participants = new List<PartyParticipant> { new() { Id = eiko, Name = "Eiko Tanaka", Driver = human ? DriverKind.User : DriverKind.LLM } };
            await GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty).AddParty(new PartyInfo { Id = partyId, Name = "world", Participants = participants });
            await GrainFactory.GetGrain<IPartyGrain>(partyId).SetParticipants(participants);

            var places = GrainFactory.GetGrain<IPlacesGrain>(partyId);
            var workplace = (await places.GetSnapshotAsync()).Places.First(p => p.Kind == PlaceKinds.Public);
            Assert.NotNull(await places.DressAsync(eiko, EikoOwn));
            var occupation = GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, eiko));
            await occupation.SetAsync(new Occupation { Title = "priestess", Workplace = workplace.Name, PlaceId = workplace.Id });
            if (designed) await occupation.SetClothesAsync(clothes ?? WorkClothesRules.Clean(Obj(ShrineDress), "priestess"));
            var self = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).Single(c => c.Id == eiko);
            return new World(this, partyId, eiko, self, occupation, new Workplaces(GrainFactory, NullLogger<Workplaces>.Instance));
        }

        private sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder.ConfigureDefaults();
                siloBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<IWorldLlm, Tailor>();
                    services.AddSingleton(Mock.Of<IStanceConsolidator>());
                    services.AddSingleton<ConsolidationService>();
                    services.AddSingleton<WorldMemory>();
                    services.AddSingleton<ISystemOneJudge>(new MenuJudge(() => null));
                    services.AddSingleton<IWorldApp, RoomsApp>();
                    services.AddSingleton<IWorldApp, MessengerApp>();
                    services.AddSingleton<IWorldApp, BoardApp>();
                    services.AddSingleton<IWorldApp, DiaryApp>();
                    services.AddSingleton<IWorldApp, CalendarApp>();
                    services.AddSingleton<IWorldApp, PlacesApp>();
                    services.AddSingleton<Workplaces>();
                    services.AddSingleton<Responsibilities>();
                });
            }
        }
    }
}
