using System.Text.Json;
using PartyTown.World.Work.Cdu;

namespace BackendTest;

/// <summary>What the CDU service really sends (ADR 0039): the fake in CduWorldTest can't catch the two sides drifting apart.</summary>
public class CduContractTest
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Status_ReadsTheTrunkThePrAndTheLastTurn()
    {
        const string json = """
        {"awake":false,"hearing":null,"queued":0,"model":"lfm2:24b",
         "trunk":{"head":"9f92336","ahead":[{"sha":"9f92336","subject":"docs: a first note","author":"Denise"}]},
         "pr":{"number":3,"url":"https://github.com/x/y/pull/3","title":"docs: a first note","state":"OPEN","head":"9f92336","checks":"pending"},
         "allowance":{"usd":0.12,"capUsd":1},"lines":5,"news":2,
         "last":{"id":"c986a759","from":{"id":"p-denise","name":"Denise"},"text":"hi","kind":"talk","status":"done","at":1,"commits":[],"reply":"Done.","asked":false,"usd":0,"doneAt":2}}
        """;
        var status = JsonSerializer.Deserialize<CduStatus>(json, Web)!;

        Assert.Equal(("Denise", 3, "pending", 5L), (status.Trunk!.Ahead[0].Author, status.Pr!.Number, status.Pr.Checks, status.Lines));
        Assert.Equal(("Denise", "Done."), (status.Last!.From.Name, status.Last.Reply));
    }

    [Fact]
    public void Status_WithNothingKnown_IsNulls()
    {
        var status = JsonSerializer.Deserialize<CduStatus>("""{"awake":true,"hearing":"Justin","queued":1,"model":"m","trunk":null,"pr":null,"allowance":null,"lines":0,"news":0,"last":null}""", Web)!;
        Assert.Equal((true, (CduTrunk?)null, (CduPr?)null), (status.Awake, status.Trunk, status.Pr));
    }

    [Fact]
    public void News_FlattensItsData()
    {
        var news = JsonSerializer.Deserialize<List<CduEntry>>("""
        [{"n":1,"at":1,"kind":"replied","text":"ok","data":{"turn":"t1","to":{"id":"p","name":"D"},"asked":true,"commits":2,"usd":null}},
         {"n":2,"at":2,"kind":"pr","text":"docs: x","data":{"number":3,"url":"u","opened":true,"commits":1}},
         {"n":3,"at":3,"kind":"rebased","text":"0 commits ahead"}]
        """, Web)!;

        Assert.Equal(("t1", true, 2), (news[0].Str("turn"), news[0].Flag("asked"), news[0].Int("commits")));
        Assert.Equal((3, true), (news[1].Int("number"), news[1].Flag("opened")));
        Assert.Null(news[2].Str("turn"));
    }
}
