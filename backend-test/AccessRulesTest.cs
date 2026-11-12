using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PartyTown.Controllers;
using PartyTown.Model;
using PartyTown.Services.Auth;
using PartyTown.Services.Realtime;
using PartyTown.World;

namespace BackendTest;

/// <summary>Who may act, speak and read: pure rules, no cluster.</summary>
public class AccessRulesTest
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid PersonaA = Guid.NewGuid();
    private static readonly Guid PersonaB = Guid.NewGuid();
    private static readonly HashSet<Guid> Humans = new() { Me, Other };

    private static ChatGroupInfo Dm(params Guid[] members)
        => new() { Id = Guid.NewGuid(), Kind = ChatGroupKinds.Direct, MemberIds = members.ToList() };

    [Fact]
    public void Acting_IsOwnPlayerOnly_UnlessAdmin()
    {
        Assert.True(AccessRules.MayActAs(Me, Me, isAdmin: false));
        Assert.False(AccessRules.MayActAs(PersonaA, Me, isAdmin: false));
        Assert.True(AccessRules.MayActAs(Other, Me, isAdmin: true));
    }

    [Fact]
    public void Speaking_AllowsUnownedPersona_NeverAnotherHuman()
    {
        Assert.True(AccessRules.MaySpeakAs(PersonaA, Me, isAdmin: false, ownedByHuman: false));
        Assert.False(AccessRules.MaySpeakAs(Other, Me, isAdmin: false, ownedByHuman: true));
    }

    [Fact]
    public void DmWithHuman_OnlyItsMembersSeeIt()
    {
        var members = AccessRules.PrivateMembers(Dm(Me, PersonaA), Humans);

        Assert.True(AccessRules.CanSeeRoom(new Viewer(Me), members));
        Assert.False(AccessRules.CanSeeRoom(new Viewer(Other), members));
        Assert.False(AccessRules.CanSeeRoom(Viewer.Anonymous, members));
    }

    [Fact]
    public void PlayerOnAuto_IsStillHuman_SoTheirDmStaysPrivate()
    {
        var cast = new List<PartyParticipant>
        {
            new() { Id = Me, Driver = DriverKind.LLM },
            new() { Id = Other, Driver = DriverKind.User },
            new() { Id = PersonaA, Driver = DriverKind.LLM },
        };

        var humans = AccessRules.Humans(cast, new[] { Me });

        Assert.Equal(new HashSet<Guid> { Me, Other }, humans);
        Assert.NotNull(AccessRules.PrivateMembers(Dm(Me, PersonaA), humans));
    }

    [Fact]
    public void AtTheKeyboard_IsAHumanPlayingThemselves_NotOnAuto_NotAPersona()
    {
        var cast = new List<PartyParticipant>
        {
            new() { Id = Me, Driver = DriverKind.LLM },
            new() { Id = Other, Driver = DriverKind.User },
            new() { Id = PersonaA, Driver = DriverKind.LLM },
        };

        Assert.True(AccessRules.AtTheKeyboard(Other, cast, new[] { Me, Other }));
        Assert.False(AccessRules.AtTheKeyboard(Me, cast, new[] { Me, Other }));
        Assert.False(AccessRules.AtTheKeyboard(PersonaA, cast, new[] { Me, Other }));
    }

    [Fact]
    public void PersonaOnlyDm_AndRooms_ArePublic()
    {
        Assert.Null(AccessRules.PrivateMembers(Dm(PersonaA, PersonaB), Humans));
        Assert.Null(AccessRules.PrivateMembers(new ChatGroupInfo { Kind = ChatGroupKinds.Room }, Humans));
        Assert.True(AccessRules.CanSeeRoom(Viewer.Anonymous, null));
    }

    [Fact]
    public void DmActivity_WithHuman_StaysWithItsTwoPeople()
    {
        var dm = new WorldActivity { App = AccessRules.MessengerApp, PersonaId = PersonaA, WithPersonaId = Me, Summary = "to Tim: \"secret\"" };

        Assert.True(AccessRules.CanSeeActivity(new Viewer(Me), dm, Humans));
        Assert.False(AccessRules.CanSeeActivity(new Viewer(Other), dm, Humans));
        Assert.False(AccessRules.CanSeeActivity(Viewer.Anonymous, dm, Humans));
    }

    [Fact]
    public void PersonaOnlyDmActivity_IsPublic_DiaryNeedsSignIn()
    {
        var dm = new WorldActivity { App = AccessRules.MessengerApp, PersonaId = PersonaA, WithPersonaId = PersonaB };
        var diary = new WorldActivity { App = AccessRules.DiaryApp, PersonaId = PersonaA };

        Assert.True(AccessRules.CanSeeActivity(Viewer.Anonymous, dm, Humans));
        Assert.False(AccessRules.CanSeeActivity(Viewer.Anonymous, diary, Humans));
        Assert.True(AccessRules.CanSeeActivity(new Viewer(Me), diary, Humans));
    }

    [Theory]
    [InlineData("  Sparky  ", "Sparky")]
    [InlineData("Big   Sparky", "Big Sparky")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void PlayerName_IsTidiedOrRefused(string? name, string? expected)
        => Assert.Equal(expected, AccessRules.CleanPlayerName(name));

    [Fact]
    public void PlayerName_HasALimit()
    {
        Assert.NotNull(AccessRules.CleanPlayerName(new string('a', AccessRules.MaxPlayerName)));
        Assert.Null(AccessRules.CleanPlayerName(new string('a', AccessRules.MaxPlayerName + 1)));
    }

    [Fact]
    public void SignedInWithoutCharacter_ReadsLikeAMember_ButSeesNoDms()
    {
        var noCharacter = new Viewer(null);
        var diary = new WorldActivity { App = AccessRules.DiaryApp, PersonaId = PersonaA };

        Assert.True(AccessRules.CanSeeActivity(noCharacter, diary, Humans));
        Assert.False(AccessRules.CanSeeRoom(noCharacter, new[] { Me, PersonaA }));
    }

    [Fact]
    public void Token_BearerBeatsCookie()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer abc";
        http.Request.Headers.Cookie = "__session=def";

        Assert.Equal("abc", ClerkAuthHandler.ReadToken(http.Request));
    }

    [Theory]
    [InlineData("flag; ph=a,b; __session=def")]
    [InlineData("__session_x1=wrong; __session=def")]
    public void Token_SurvivesCookiesTheSdkParserTripsOn(string cookie)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = cookie;

        Assert.Equal("def", ClerkAuthHandler.ReadToken(http.Request));
    }

    [Fact]
    public void Token_MissingIsNull()
        => Assert.Null(ClerkAuthHandler.ReadToken(new DefaultHttpContext().Request));

    [Fact]
    public void EveryFrameNamingARoom_IsRoomScoped()
    {
        var unscoped = typeof(IPartyRealtimeData).Assembly.GetTypes()
            .Where(t => typeof(IPartyRealtimeData).IsAssignableFrom(t) && t.IsClass)
            .Where(t => t.GetProperty("ChatGroupId", BindingFlags.Public | BindingFlags.Instance) is not null)
            .Where(t => t != typeof(PartyPongData) && !typeof(IRoomFrame).IsAssignableFrom(t));

        Assert.Empty(unscoped);
    }

    [Fact]
    public void Frames_FollowRoomAndActivityRules()
    {
        var room = Guid.NewGuid();
        var message = new RealtimeFrame("party.message.created", new PartyMessageCreatedData(Guid.Empty, room, null));
        var members = new[] { Me, PersonaA };

        Assert.False(FeedPrivacy.CanSee(Viewer.Anonymous, message, members, Humans));
        Assert.True(FeedPrivacy.CanSee(new Viewer(Me), message, members, Humans));
        Assert.True(FeedPrivacy.CanSee(Viewer.Anonymous, message, null, Humans));
    }

    [Fact]
    public void WorldFrames_HideDiaryAndHumanDms()
    {
        var diary = new RealtimeFrame("world.diary", World("diary", "{}"));
        var activity = new RealtimeFrame("world.activity", World("activity",
            $$"""{"app":"messenger","personaId":"{{PersonaA}}","withPersonaId":"{{Me}}","summary":"secret"}"""));
        var clock = new RealtimeFrame("world.clock", World("clock", "{}"));

        Assert.False(FeedPrivacy.CanSee(Viewer.Anonymous, diary, null, Humans));
        Assert.True(FeedPrivacy.CanSee(new Viewer(Other), diary, null, Humans));
        Assert.False(FeedPrivacy.CanSee(new Viewer(Other), activity, null, Humans));
        Assert.True(FeedPrivacy.CanSee(new Viewer(Me), activity, null, Humans));
        Assert.True(FeedPrivacy.CanSee(Viewer.Anonymous, clock, null, Humans));
    }

    [Fact]
    public void AnonymousActions_AreAllReads()
    {
        var writes = Actions()
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(h => h is not HttpGetAttribute))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}");

        Assert.Empty(writes);
    }

    [Theory]
    [InlineData(typeof(LlmConfigController), nameof(LlmConfigController.GetUsage))]
    [InlineData(typeof(WorldController), nameof(WorldController.GetDiary))]
    [InlineData(typeof(WorldController), nameof(WorldController.GetDirectChats))]
    [InlineData(typeof(PartyController), nameof(PartyController.GetPapertrail))]
    [InlineData(typeof(PartyController), nameof(PartyController.GetModels))]
    [InlineData(typeof(MemoryController), nameof(MemoryController.GetGraph))]
    public void PrivateReads_StayBehindSignIn(Type controller, string action)
        => Assert.Null(controller.GetMethod(action)!.GetCustomAttribute<AllowAnonymousAttribute>());

    [Theory]
    [InlineData(typeof(LlmConfigController), nameof(LlmConfigController.UpdateProvider))]
    [InlineData(typeof(PersonaController), nameof(PersonaController.RealtimeGeneration))]
    [InlineData(typeof(WorldController), nameof(WorldController.Update))]
    [InlineData(typeof(WorldController), nameof(WorldController.RemovePlayer))]
    public void WorldControls_AreAdminOnly(Type controller, string action)
        => Assert.Equal(ClerkAuth.AdminPolicy, controller.GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

    private static IEnumerable<MethodInfo> Actions()
        => typeof(WorldController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    private static PartyWorldEventData World(string kind, string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return new PartyWorldEventData(Guid.Empty, kind, doc.RootElement.Clone());
    }
}
