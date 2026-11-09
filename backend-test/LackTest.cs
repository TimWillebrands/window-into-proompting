using PartyTown.World;
using PartyTown.World.Desires;
using PartyTown.World.Work.Cdu;
using PartyTown.World.Work.Companies;

namespace BackendTest;

/// <summary>Reaching past the arena (ADR 0047): felt rarely, voiced plainly, and on the Cytheurgists' desk.</summary>
public class LackTest
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Theory]
    [InlineData(0.9, null, true)]
    [InlineData(0.6, null, false)]
    [InlineData(0.9, 1, false)]
    [InlineData(0.9, 7, true)]
    public void Felt_StrongAndNotLately(double p, int? hoursAgo, bool felt)
        => Assert.Equal(felt, Lack.Felt(p, hoursAgo is { } h ? Now.AddHours(-h).ToUnixTimeMilliseconds() : null, Now));

    [Theory]
    [InlineData(null, null)]
    [InlineData("none", null)]
    [InlineData("None, the walk was it.", null)]
    [InlineData("\"send Mara the photo of the boat\"", "send Mara the photo of the boat")]
    public void Read_NoneIsNothing(string? said, string? read) => Assert.Equal(read, Lack.Read(said));

    [Fact]
    public void Of_OnePerPerson_NewestFirst()
    {
        var mara = Guid.NewGuid();
        var acts = new[]
        {
            new WorldActivity { PersonaId = mara, PersonaName = "Mara", Action = Lack.Action, Summary = "old", Ts = Now.AddHours(-3).ToUnixTimeMilliseconds() },
            new WorldActivity { PersonaId = mara, PersonaName = "Mara", Action = Lack.Action, Summary = "sell a painting", Ts = Now.ToUnixTimeMilliseconds() },
            new WorldActivity { PersonaId = Guid.NewGuid(), PersonaName = "Dan", Action = "post", Summary = "hi", Ts = Now.ToUnixTimeMilliseconds() },
        };
        Assert.Equal(new[] { ("Mara", "sell a painting") }, Lack.Of(acts));
    }

    [Fact]
    public void Desk_ShowsWhatPeopleFoundNoWayTo()
    {
        var shop = new Company { Id = Guid.NewGuid(), Name = "Vormwerk", Mandate = "keeps the Forms", Kind = CompanyKinds.Cdu };
        var acts = new[] { new WorldActivity { PersonaId = Guid.NewGuid(), PersonaName = "Mara", App = "life", Action = Lack.Action, Summary = "sell a painting to Pieter", Ts = Now.ToUnixTimeMilliseconds() } };
        var text = CduScreen.Render(new CduDesk(shop, new CduState(), null, CduScreen.Health(acts, Now), Guid.NewGuid()));
        Assert.Contains("found no way to here:\n- Mara: sell a painting to Pieter", text);
    }
}
