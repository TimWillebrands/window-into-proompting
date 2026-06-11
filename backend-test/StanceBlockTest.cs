using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

/// <summary>
/// The ambient "# Where you stand" block renderer (ADR 0016) — the same helper both the
/// Decision and Speaking prompts call, so a single test pins the rendered shape both phases
/// see.
/// </summary>
public sealed class StanceBlockTest
{
    [Fact]
    public void Render_Empty_OmitsBlock()
    {
        Assert.Equal(string.Empty, StanceBlock.Render(null));
        Assert.Equal(string.Empty, StanceBlock.Render(Array.Empty<string>()));
    }

    [Fact]
    public void Render_Lines_ProduceHeadedBulletBlock()
    {
        var block = StanceBlock.Render(new[]
        {
            "Vlad exaggerates — you've watched him inflate three stories",
            "Lisp is elegant once it clicks",
        });

        Assert.Contains("# Where you stand", block);
        Assert.Contains("- Vlad exaggerates — you've watched him inflate three stories", block);
        Assert.Contains("- Lisp is elegant once it clicks", block);
    }

    [Fact]
    public void Render_TrimsEachLine()
    {
        var block = StanceBlock.Render(new[] { "  you trust Hana completely  " });
        Assert.Contains("- you trust Hana completely\n", block);
        Assert.DoesNotContain("-   you trust", block);
    }
}
