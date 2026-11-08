using System.Text.Json.Nodes;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>ADR 0023 making and customising: poses, restyling, generated things and Places, arranging.</summary>
public partial class PlacesTest
{
    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public async Task APairPose_TakesBoth_AndReadsInPlainWords_UntilOneWalksOff()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var centre = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the city centre");
        await places.MoveAsync(denise, centre.Id, "bridge");
        await places.MoveAsync(pieter, centre.Id, "bridge");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(await places.SetPoseAsync(denise, Poses.Dance, null));
        Assert.Equal("leaning on the bridge", await places.SetPoseAsync(denise, Poses.Lean, null));
        Assert.Equal("hugging Pieter on the bridge", await places.SetPoseAsync(denise, Poses.Hug, pieter));

        Assert.Contains("hugging Pieter on the bridge", await places.GetSituationAsync(denise));
        Assert.Contains("Denise (right next to you, hugging you on the bridge)", await places.GetSituationAsync(pieter));

        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await places.MoveAsync(pieter, cafe.Id, null);
        var bodies = (await places.GetSnapshotAsync()).Bodies;
        Assert.All(bodies, b => Assert.Null(b.Pose));
    }

    [Fact]
    public async Task Restyling_DrawsThePrintsItAsked_AndKeepsTheRestOfTheDesign()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        await places.GetSnapshotAsync();
        var answers = new Queue<string>(new[]
        {
            """{"picks": {"topStyle": "band-tee", "top": "black"}, "summary": "A black band tee.", "design": {"shoes": {"kind": "platform", "color": "#111111"}, "top": {"color": "#111111", "prints": [{"at": "chest", "brief": "a skull"}, {"at": "back", "brief": "a bat"}]}}}""",
            """{"picks": {}, "summary": "A red band tee.", "design": {"top": {"color": "#aa0000"}}}""",
        });
        ScriptedLlm.Instance.Json = (system, _) => system == FigureDesign.RestylePrompt ? Obj(answers.Dequeue()) : null;
        ScriptedLlm.Instance.Text = (system, brief) => system == FigureDesign.PrintPrompt && brief == "a skull" ? """Here: <svg><circle r="9"/></svg>""" : "no can do";
        try
        {
            var first = await places.RestyleAsync(denise, "a band tee with a skull");
            var second = await places.RestyleAsync(denise, "make the tee red");

            var drawn = Obj(first!.Look.Design!);
            var print = Assert.Single(drawn["top"]!["prints"]!.AsArray());
            Assert.Contains("circle", print!["svg"]!.ToString());
            Assert.Equal("A black band tee.", first.Look.Summary);

            var after = Obj(second!.Look.Design!);
            Assert.Equal("#aa0000", after["top"]!["color"]!.ToString());
            Assert.Equal("platform", after["shoes"]!["kind"]!.ToString());
            Assert.Contains("A red band tee.", second.Done);
        }
        finally
        {
            ScriptedLlm.Instance.Json = (_, _) => null;
            ScriptedLlm.Instance.Text = (_, _) => null;
        }
    }

    [Fact]
    public async Task ALookFromBeforeFigures_IsDrawnAnew_AsTheSamePerson()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        await places.SetAppearanceAsync(denise, new Dictionary<string, string?> { ["hairStyle"] = "afro" });
        ScriptedLlm.Instance.Json = (system, user) =>
        {
            if (system != FigureDesign.Prompt || !user.Contains("Denise")) return null;
            return Obj("""{"picks": {"hairStyle": "afro"}, "summary": "Big afro, plain tee.", "design": {"top": {"color": "#123456"}}}""");
        };
        try
        {
            await places.RedressAsync(denise);
            Appearance? look = null;
            for (var i = 0; i < 50 && look?.Design is null; i++)
            {
                await Task.Delay(100);
                look = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise).Appearance;
            }

            Assert.Contains("123456", look!.Design);
            Assert.Equal("Big afro, plain tee.", look.Summary);
            Assert.Equal("afro", look.HairStyle);
        }
        finally
        {
            ScriptedLlm.Instance.Json = (_, _) => null;
        }
    }

    [Fact]
    public async Task Restyling_ChangesOnlyWhatWasAsked_WithinThePalette()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var before = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise).Appearance
            ?? AppearanceRules.Default(denise);
        ScriptedLlm.Instance.Json = (system, user) => system == FigureDesign.RestylePrompt
            ? Obj($$"""{"hair": "pink", "topStyle": "hoodie", "skin": "{{before.Skin}}", "shoes": "glitter"}""")
            : null;
        try
        {
            var result = await places.RestyleAsync(denise, "dye my hair pink and get a hoodie");

            Assert.NotNull(result);
            Assert.Equal("pink", result.Look.Hair);
            Assert.Equal("hoodie", result.Look.TopStyle);
            Assert.Equal(before.Shoes, result.Look.Shoes);
            Assert.Contains("pink hair", result.Done);
        }
        finally
        {
            ScriptedLlm.Instance.Json = (_, _) => null;
        }
    }

    [Fact]
    public async Task AThing_GetsAGeneratedModel_CleanedToFitItsTile()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        ScriptedLlm.Instance.Json = (system, _) => system == ThingModel.Prompt
            ? Obj("""{"parts": [{"box": [5, 0.4, 0.3], "at": [3, 0, 0], "c": "#ff0000"}, {"teapot": 1}, {"ball": 0.1, "c": "not a colour"}]}""")
            : null;
        try
        {
            var thing = await places.AddThingAsync(cafe.Id, denise, "a lava lamp shaped like a cat");
            Assert.NotNull(thing);

            string? model = null;
            for (var i = 0; i < 50 && model is null; i++)
            {
                await Task.Delay(50);
                model = (await places.GetSnapshotAsync()).Furni.Single(f => f.FurniId == thing.FurniId).Model;
            }
            var parts = JsonNode.Parse(model!)!["parts"]!.AsArray();
            Assert.Equal(2, parts.Count);
            Assert.Equal(1, parts[0]!["box"]![0]!.GetValue<double>());
            Assert.Equal(0.5, parts[0]!["at"]![0]!.GetValue<double>());
            Assert.Equal("#999999", parts[1]!["c"]!.ToString());
        }
        finally
        {
            ScriptedLlm.Instance.Json = (_, _) => null;
        }
    }

    [Fact]
    public async Task OnlyTheOwner_ArrangesTheirHome()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var snapshot = await places.GetSnapshotAsync();
        var home = snapshot.Places.Single(p => p.OwnerId == denise);
        var plant = home.Furni.Single(f => f.Kind == FurniKinds.Plant);

        Assert.False(await places.ArrangeAsync(pieter, home.Id, plant.Id, 3, 3, 1));
        Assert.True(await places.ArrangeAsync(denise, home.Id, plant.Id, 3, 3, 5));
        Assert.True(await places.SetPlaceStyleAsync(denise, home.Id, "#AABBCC", "nope"));

        snapshot = await places.GetSnapshotAsync();
        var view = snapshot.Furni.Single(f => f.PlaceId == home.Id && f.FurniId == plant.Id);
        Assert.Equal((3, 3, 1), (view.TileX, view.TileZ, view.Rot));
        home = snapshot.Places.Single(p => p.Id == home.Id);
        Assert.Equal(("#aabbcc", (string?)null), (home.WallColour, home.FloorColour));
    }

    [Fact]
    public async Task AGeneratedPlace_IsRetriedUntilItChecksOut_ThenWalkable()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var tries = 0;
        ScriptedLlm.Instance.Json = (system, _) =>
        {
            if (!system.Contains("You design one new place")) return null;
            tries++;
            var spotAt = tries == 1 ? "[20, 20]" : "[4, 3]";
            return Obj("""
                {"name": "the Rooftop Bar", "at": "at", "description": "a bar on a roof, fairy lights everywhere",
                 "spots": [{"id": "bar", "name": "the bar", "at": "at", "capacity": 3, "seat": false, "poses": ["lean", "fly"]},
                           {"id": "Dance Floor", "name": "the dance floor", "at": "on", "capacity": 4, "poses": ["dance"]}],
                 "scene": {"map": ["#ww######", "#.......#", "#.......#", "D.......#", "#.......#", "#########"],
                           "props": [{"p": "counter", "at": [1, 1]}, {"p": "dance-floor", "at": [4, 2]}],
                           "spots": {"bar": {"at": [2, 2]}, "dance-floor": {"at": SPOT}}}}
                """.Replace("SPOT", spotAt));
        };
        try
        {
            var place = await places.CreatePlaceAsync(denise, "a rooftop bar with a dance floor", outdoor: false);

            Assert.True(place is not null, string.Join("\n---\n", ScriptedLlm.Instance.Users.TakeLast(3)) + $" tries={tries}");
            Assert.Equal(2, tries);
            Assert.Contains("dance-floor' at 20,20 is off the map", ScriptedLlm.Instance.Users.Last(u => u.StartsWith("Design:")));
            Assert.Equal(new[] { "bar", "dance-floor" }, place.Spots.Select(s => s.Id));
            Assert.Equal(new[] { "lean" }, place.Spots[0].Poses);
            Assert.Equal(denise, place.MadeBy);
            Assert.NotNull(place.CustomScene);

            await places.MoveAsync(denise, place.Id, "dance-floor");
            _fx.Time.Advance(TimeSpan.FromMinutes(2));
            Assert.StartsWith("You're at the Rooftop Bar, standing on the dance floor.", await places.GetSituationAsync(denise));
            Assert.Equal("dancing on the dance floor", await places.SetPoseAsync(denise, Poses.Dance, null));

            Assert.True(await places.RemovePlaceAsync(place.Id));
            var after = await places.GetSnapshotAsync();
            Assert.DoesNotContain(after.Places, p => p.Id == place.Id);
            Assert.Equal(after.Places.Single(p => p.OwnerId == denise).Id, after.Bodies.Single(b => b.ParticipantId == denise).PlaceId);
        }
        finally
        {
            ScriptedLlm.Instance.Json = (_, _) => null;
        }
    }
    [Fact]
    public async Task ARedesignedHome_KeepsItsPeopleAndThings_InANewScene()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var home = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == denise);
        var plant = home.Furni.Single(f => f.Kind == FurniKinds.Plant);
        Assert.True(await places.ArrangeAsync(denise, home.Id, plant.Id, 3, 3, 1));
        var withGarden = true;
        ScriptedLlm.Instance.Json = (system, _) => !system.Contains("You design one new place") ? null : Obj("""
            {"name": "the tower", "description": "a round stone room full of books",
             "spots": [{"id": "sofa", "name": "the reading chair", "at": "in", "capacity": 1, "seat": true},
                       {"id": "desk", "name": "the lectern", "at": "at", "capacity": 1},
                       {"id": "kitchen", "name": "the hearth", "at": "by", "capacity": 2},
                       {"id": "bed", "name": "the bed", "at": "on", "capacity": 1, "seat": true}GARDEN],
             "scene": {"map": ["#ww#######", "#........#", "#........#", "D........#", "#ggggg...#", "#ggtgg...#", "##########"],
                       "spots": {"sofa": {"at": [2, 1]}, "desk": {"at": [7, 1]}, "kitchen": {"at": [7, 3]}, "bed": {"at": [4, 2]}, "garden": {"at": [2, 4]}},
                       "floor": "#5a4a3a", "wall": "#8a8a90"}}
            """.Replace("GARDEN", withGarden ? """, {"id": "garden", "name": "the garden", "at": "in", "capacity": 2}""" : ""));
        try
        {
            var redone = await places.RedesignHomeAsync(home.Id, new string('x', 1000) + " a tower", "the stargazer's tower", false);

            Assert.NotNull(redone);
            Assert.Contains("Spots must include these ids", ScriptedLlm.Instance.Users.Last(u => u.StartsWith("Design:")));
            Assert.Equal((home.Id, denise, "the stargazer's tower"), (redone.Id, redone.OwnerId, redone.Name));
            Assert.Equal("custom", redone.Scene);
            Assert.Contains(redone.Spots, s => s.Id == "garden");
            var snapshot = await places.GetSnapshotAsync();
            var view = snapshot.Furni.Single(f => f.PlaceId == home.Id && f.FurniId == plant.Id);
            Assert.Equal((null, null), (view.TileX, view.TileZ));

            await places.MoveAsync(denise, home.Id, "garden");
            _fx.Time.Advance(TimeSpan.FromMinutes(5));
            withGarden = false;
            Assert.NotNull(await places.RedesignHomeAsync(home.Id, "a tower without a garden", null, false));
            snapshot = await places.GetSnapshotAsync();
            var body = snapshot.Bodies.Single(b => b.ParticipantId == denise);
            Assert.Equal((home.Id, "sofa"), (body.PlaceId, body.SpotId));
            Assert.Equal("the stargazer's tower", snapshot.Places.Single(p => p.Id == home.Id).Name);
        }
        finally
        {
            ScriptedLlm.Instance.Json = (_, _) => null;
        }
    }

    [Fact]
    public async Task AnOutdoorHome_HasNoWalls_AndItsThingsGoByTheDesignsWords()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var home = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == denise);
        string? system = null;
        ScriptedLlm.Instance.Json = (s, _) =>
        {
            if (!s.Contains("You design one new place")) return null;
            system = s;
            return Obj("""
                {"name": "the camp", "description": "a tarp strung between a lamp post and a canal railing",
                 "spots": [{"id": "sofa", "name": "the upturned crate", "at": "on", "capacity": 2, "seat": true},
                           {"id": "desk", "name": "the soapbox", "at": "on", "capacity": 1},
                           {"id": "kitchen", "name": "the camping stove", "at": "by", "capacity": 2},
                           {"id": "bed", "name": "the bedroll", "at": "on", "capacity": 1}],
                 "things": {"plant": "the geranium in a tin can", "coffee": "the dented kettle on the stove", "bed": "the bedroll under the tarp"},
                 "scene": {"map": ["HHHHHHHH", "--------", "--------", "--------", "--------", "--------", "--------"],
                           "entrances": {"*": [0, 6]},
                           "spots": {"sofa": {"at": [1, 2]}, "desk": {"at": [5, 2]}, "kitchen": {"at": [2, 5]}, "bed": {"at": [6, 5]}}}}
                """);
        };
        try
        {
            var camp = await places.RedesignHomeAsync(home.Id, "a street camp, a modern Diogenes", "Denise's camp", true);

            Assert.NotNull(camp);
            Assert.Contains("Outdoors: no walls", system);
            Assert.True(JsonNode.Parse(camp.CustomScene!)!["outdoor"]!.GetValue<bool>());
            var furni = (await places.GetSnapshotAsync()).Furni.Where(f => f.PlaceId == home.Id).ToDictionary(f => f.FurniId, f => f.Name);
            Assert.Equal("the geranium in a tin can", furni["plant"]);
            Assert.Equal("the bedroll under the tarp", furni["bed"]);
            Assert.Equal("the laptop", furni["laptop"]);
        }
        finally
        {
            ScriptedLlm.Instance.Json = (_, _) => null;
        }
    }
}
