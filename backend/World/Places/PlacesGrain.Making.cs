using System.Text.Json.Nodes;
using PartyTown.Grains.Generation;

namespace PartyTown.World.Places;

[GenerateSerializer, Alias("PartyTown.World.Places.RestyleResult")]
public sealed record RestyleResult([property: Id(0)] Appearance Look, [property: Id(1)] string Done);

/// <summary>Customisation and making (ADR 0023): poses, restyling, generated things and Places, arranging a Home.</summary>
public sealed partial class PlacesGrain
{
    public const int MaxMadePlaces = 12;
    private readonly Queue<(Guid PlaceId, string FurniId, string Description)> _models = new();
    private bool _modelling;

    // ---- Poses ----

    public async Task<string?> SetPoseAsync(Guid participantId, string? pose, Guid? with)
    {
        await SettleAsync();
        if (!state.State.Bodies.TryGetValue(participantId, out var body) || body.Asleep) return null;
        if (pose is null)
        {
            EndPose(body);
            await SaveAsync();
            await PublishAsync(body);
            return "relaxes";
        }
        var place = Find(body.DestPlaceId ?? body.PlaceId);
        var spot = place?.Spots.FirstOrDefault(s => s.Id == (body.DestSpotId ?? body.SpotId));
        if (place is null || spot is null) return null;
        var company = Occupants(place.Id).Where(b => b.ParticipantId != participantId && (b.DestSpotId ?? b.SpotId) == spot.Id).ToList();
        if (!Poses.Allowed(spot, company.Count > 0).Contains(pose)) return null;
        Body? partner = null;
        if (Poses.Pair.Contains(pose))
        {
            partner = with is { } w ? company.FirstOrDefault(b => b.ParticipantId == w) : company.FirstOrDefault();
            if (partner is null || partner.IsWalking || partner.Asleep) return null;
        }
        EndPose(body);
        Apply(body, pose, partner?.ParticipantId);
        if (partner is not null)
        {
            EndPose(partner);
            Apply(partner, pose, body.ParticipantId);
            await PublishAsync(partner);
        }
        await SaveAsync();
        await PublishAsync(body);
        return Poses.Describe(pose, spot, partner?.Name.Split(' ')[0]);
    }

    private void Apply(Body b, string pose, Guid? with)
    {
        b.Pose = pose;
        b.PoseWith = with;
        b.PoseUntil = Poses.Lasts(pose) is { } t ? Now() + (long)t.TotalMilliseconds : null;
        if (!b.IsWalking) b.Posture = Poses.PostureOf(pose, b.Posture);
    }

    /// <summary>Drop a pose, and the partner's side of a pair pose.</summary>
    private void EndPose(Body b)
    {
        if (b.Pose is null) return;
        if (b.PoseWith is { } w && state.State.Bodies.TryGetValue(w, out var other) && other.PoseWith == b.ParticipantId)
        {
            other.Pose = null;
            other.PoseWith = null;
            other.PoseUntil = null;
            _ = PublishAsync(other);
        }
        var sitting = b.Pose is Poses.Lie or Poses.Lounge or Poses.Perch or Poses.CrossLegged;
        b.Pose = null;
        b.PoseWith = null;
        b.PoseUntil = null;
        if (sitting && !b.IsWalking && !b.Asleep)
        {
            var spot = Find(b.PlaceId)?.Spots.FirstOrDefault(s => s.Id == b.SpotId);
            b.Posture = spot?.Seat == true ? Posture.Sitting : Posture.Standing;
        }
    }

    // ---- Looks ----

    public async Task<RestyleResult?> RestyleAsync(Guid participantId, string intent)
    {
        if (string.IsNullOrWhiteSpace(intent) || !state.State.Bodies.TryGetValue(participantId, out var body)) return null;
        var before = body.Appearance ?? AppearanceRules.Default(participantId);
        var json = await llm.CompleteJsonAsync(JobComplexity.General, FigureDesign.RestylePrompt,
            $"Current look: {AppearanceRules.Describe(before)}\nDrawn as: {WorldStream.Clip(before.Design ?? "(palette only)", 5000)}\nWhat they want: {WorldStream.Clip(intent, 300)}",
            CancellationToken.None, 0.5);
        if (json is null) return null;
        var design = await DrawPrintsAsync(FigureDesign.Clean(json["design"]));
        if (!state.State.Bodies.TryGetValue(participantId, out body)) return null;
        var after = Dressed(before, json, design);
        var changes = AppearanceRules.Changes(before, after);
        if (changes.Length == 0 && after.Design == before.Design) return new RestyleResult(before, "looks in the mirror and keeps their look");
        if (changes.Length == 0) changes = after.Summary ?? "a new look";
        body.Appearance = after;
        body.Activity = "showing off a new look";
        body.ActivityUntil = Now() + (long)ActivityWearsOff.TotalMilliseconds;
        await SaveAsync();
        await PublishAsync(body);
        return new RestyleResult(after, $"changes their look: {changes}");
    }

    // ---- Things ----

    public async Task<FurniView?> AddThingAsync(Guid placeId, Guid? ownerId, string description)
    {
        await SettleAsync();
        var place = Find(placeId);
        if (place is null || string.IsNullOrWhiteSpace(description)) return null;
        var now = Now();
        var decor = place.Furni.Where(f => f.Kind == FurniKinds.Decor).OrderBy(f => f.TendedAt).ToList();
        if (decor.Count >= Decor.MaxInHome * 2) place.Furni.Remove(decor[0]);
        var spot = place.Spots[Random.Shared.Next(place.Spots.Count)];
        var furni = new Furni
        {
            Id = $"thing-{now}-{Random.Shared.Next(1000)}", Kind = FurniKinds.Decor, Style = Decor.StyleOf(description),
            Name = Decor.Label(description), SpotId = spot.Id, TendedAt = now, OwnerId = ownerId,
        };
        place.Furni.Add(furni);
        await SaveAsync();
        await PublishPlaceAsync(place);
        QueueModel(place, furni, description);
        return View(place, furni, now);
    }

    /// <summary>Most asked-for first ("3× a bike poster"), then newest.</summary>
    public Task<List<string>> GetCatalogueMissesAsync()
        => Task.FromResult(Enumerable.Reverse(state.State.CatalogueMisses)
            .Select((m, i) => (Text: m, Count: state.State.MissCounts.GetValueOrDefault(m, 1), Order: i))
            .OrderByDescending(m => m.Count).ThenBy(m => m.Order)
            .Select(m => m.Count > 1 ? $"{m.Count}× {m.Text}" : m.Text).ToList());

    private void NoteMiss(string description)
    {
        var text = WorldStream.Clip(description, 120);
        var misses = state.State.CatalogueMisses;
        misses.Remove(text);
        misses.Add(text);
        state.State.MissCounts[text] = state.State.MissCounts.GetValueOrDefault(text) + 1;
        if (misses.Count > 40)
        {
            state.State.MissCounts.Remove(misses[0]);
            misses.RemoveAt(0);
        }
    }

    /// <summary>A catalogue model the description names; System One picks among close ones (or none), else the best word match.</summary>
    private async Task<string?> FromCatalogueAsync(string description)
    {
        var candidates = CatalogueMatch.Candidates(description, CatalogueMatch.Catalogue);
        if (candidates.Count == 0) return null;
        try
        {
            var options = candidates.ToDictionary(c => c.Name, c => (string?)c.About);
            options["none"] = "none of these is really it";
            var answers = await judge.AskAsync($"Someone puts up or gets: \"{WorldStream.Clip(description, 200)}\"",
                new Dictionary<string, Services.SystemOne.SystemOneQuestion>
                {
                    ["thing"] = new Services.SystemOne.ChoiceQuestion("Which of these models shows it best?", options),
                }, CancellationToken.None);
            return answers.SampleChoice("thing", 0.2) switch
            {
                null => candidates[0].Name,
                "none" => null,
                var pick => pick,
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Catalogue pick failed for {Thing}; using the best word match", description);
            return candidates[0].Name;
        }
    }

    /// <summary>Author: make a thing's model again.</summary>
    public Task RemodelAsync(Guid placeId, string furniId)
    {
        if (Find(placeId) is { } place && place.Furni.FirstOrDefault(f => f.Id == furniId) is { } furni)
            QueueModel(place, furni, furni.Name);
        return Task.CompletedTask;
    }

    private void QueueModel(Place place, Furni furni, string description)
    {
        _models.Enqueue((place.Id, furni.Id, description));
        if (!_modelling) _ = ModelInTurnAsync();
    }

    private async Task ModelInTurnAsync()
    {
        _modelling = true;
        try
        {
            while (_models.TryDequeue(out var job)) await ModelAsync(job.PlaceId, job.FurniId, job.Description);
        }
        finally
        {
            _modelling = false;
        }
    }

    /// <summary>The catalogue first (no LLM); otherwise one LLM call: a few coloured primitives, cleaned to fit a tile. The kind's prefab stands in until (or unless) it lands.</summary>
    private async Task ModelAsync(Guid placeId, string furniId, string description)
    {
        try
        {
            if (await FromCatalogueAsync(description) is { } prefab)
            {
                if (Find(placeId) is not { } at || at.Furni.FirstOrDefault(f => f.Id == furniId) is not { } thing) return;
                thing.Prefab = prefab;
                await SaveAsync();
                await PublishPlaceAsync(at);
                return;
            }
            NoteMiss(description);
            var json = await llm.CompleteJsonAsync(JobComplexity.General, ThingModel.Prompt,
                $"Make: {WorldStream.Clip(description, 200)}", CancellationToken.None, 0.7);
            var model = ThingModel.Clean(json);
            if (model is null || Find(placeId) is not { } place || place.Furni.FirstOrDefault(f => f.Id == furniId) is not { } furni) return;
            furni.Model = model;
            await SaveAsync();
            await PublishPlaceAsync(place);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Modelling {Thing} failed; it keeps its stand-in", description);
        }
    }

    /// <summary>Owners (and the author, <see cref="Guid.Empty"/>) arrange things: a tile and quarter turns, or back to the scene's choice with null.</summary>
    public async Task<bool> ArrangeAsync(Guid participantId, Guid placeId, string furniId, int? x, int? z, int rot)
    {
        if (Find(placeId) is not { } place || place.Furni.FirstOrDefault(f => f.Id == furniId) is not { } furni) return false;
        if (!MayArrange(place, furni, participantId)) return false;
        furni.TileX = x is null ? null : Math.Clamp(x.Value, 0, PlaceDesign.MaxSide - 1);
        furni.TileZ = z is null ? null : Math.Clamp(z.Value, 0, PlaceDesign.MaxSide - 1);
        furni.Rot = ((rot % 4) + 4) % 4;
        await SaveAsync();
        await PublishPlaceAsync(place);
        return true;
    }

    public async Task<bool> RemoveThingAsync(Guid participantId, Guid placeId, string furniId)
    {
        if (Find(placeId) is not { } place || place.Furni.FirstOrDefault(f => f.Id == furniId) is not { Kind: FurniKinds.Decor } furni) return false;
        if (!MayArrange(place, furni, participantId)) return false;
        place.Furni.Remove(furni);
        await SaveAsync();
        await PublishPlaceAsync(place);
        return true;
    }

    public async Task<bool> SetPlaceStyleAsync(Guid participantId, Guid placeId, string? wall, string? floor)
    {
        if (Find(placeId) is not { } place) return false;
        if (participantId != Guid.Empty && !place.LivesIn(participantId) && place.MadeBy != participantId) return false;
        place.WallColour = Hex(wall);
        place.FloorColour = Hex(floor);
        await SaveAsync();
        await PublishPlaceAsync(place);
        return true;
    }

    private static bool MayArrange(Place place, Furni furni, Guid who)
        => who == Guid.Empty || place.LivesIn(who) || place.MadeBy == who || (furni.OwnerId == who && place.Kind == PlaceKinds.Public);

    private static string? Hex(string? c)
        => c is { Length: 7 } && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit) ? c.ToLowerInvariant() : null;

    // ---- Places ----

    /// <summary>The model designs spots and a scene from a description; up to three tries, each told what didn't check out.</summary>
    public async Task<Place?> CreatePlaceAsync(Guid? madeBy, string description, bool outdoor)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        if (state.State.Places.Count(p => p.CustomScene is not null && p.Kind == PlaceKinds.Public) >= MaxMadePlaces) return null;
        if (await DesignAsync(description, outdoor) is not { } draft) return null;
        var json = draft.Json;
        var name = json["name"]?.ToString()?.Trim() is { Length: > 0 } n ? WorldStream.Clip(n, 40) : "the new place";
        if (!name.StartsWith("the ", StringComparison.OrdinalIgnoreCase) && !char.IsUpper(name[0])) name = $"the {name}";
        if (state.State.Places.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"{name} ({state.State.Places.Count})";
        var place = new Place
        {
            Id = Guid.NewGuid(), Name = name, Kind = PlaceKinds.Public, MadeBy = madeBy,
            At = json["at"]?.ToString() is "in" or "on" ? json["at"]!.ToString() : "at",
            Description = WorldStream.Clip(json["description"]?.ToString() ?? description, 200),
            Purpose = json["purpose"]?.ToString()?.Trim() is { Length: > 0 } purpose ? WorldStream.Clip(purpose, 120) : null,
            Spots = draft.Spots, CustomScene = PlaceDesign.Store(draft.Scene), Scene = "custom",
            DistrictId = Districts.Centre,
        };
        state.State.Places.Add(place);
        await SaveAsync();
        await PublishPlaceAsync(place);
        return place;
    }

    /// <summary>Asks the designer until the scene checks out, three tries at most; null when it never does.</summary>
    /// <param name="rule">What's wrong with these spots beyond the scene check; null when they're fine.</param>
    private async Task<PlaceDraft?> DesignAsync(string description, bool outdoor, Func<IReadOnlyList<Spot>, string?>? rule = null)
    {
        var user = $"Design: {WorldStream.Clip(description, 400)}\nPlaces that already exist: {string.Join(", ", state.State.Places.Where(p => p.Kind == PlaceKinds.Public).Select(p => p.Name))}";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            JsonObject? json;
            try
            {
                json = await llm.CompleteJsonAsync(JobComplexity.General, PlaceDesign.Prompt(outdoor), user, CancellationToken.None, 0.8);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Designing a Place failed");
                return null;
            }
            if (json?["scene"] is not JsonObject scene) { user += "\n\nYour last answer had no \"scene\" object. Try again."; continue; }
            var spots = PlaceDesign.SpotsOf(json["spots"] as JsonArray);
            var errors = spots.Count < 2 ? new List<string> { "give 2 to 5 spots" } : PlaceDesign.Check(scene, spots);
            if (rule?.Invoke(spots) is { } broken) errors.Add(broken);
            if (errors.Count > 0)
            {
                user += $"\n\nYour last design didn't work:\n- {string.Join("\n- ", errors.Take(8))}\nFix these and output the whole JSON again.";
                continue;
            }
            return new PlaceDraft(json, scene, spots);
        }
        return null;
    }

    private sealed record PlaceDraft(JsonObject Json, JsonObject Scene, List<Spot> Spots);

    public async Task<string?> MakeArtAsync(Guid participantId, string description)
    {
        await SettleAsync();
        if (string.IsNullOrWhiteSpace(description)) return null;
        if (!state.State.Bodies.TryGetValue(participantId, out var body) || body.IsWalking) return null;
        var place = Find(body.PlaceId);
        var now = Now();
        var midnight = new DateTimeOffset(time.GetLocalNow().Date, time.GetLocalNow().Offset).ToUnixTimeMilliseconds();
        if (place is null || !Art.CanMake(place, participantId, now, midnight)) return null;

        var onShow = place.Furni.Where(f => f.Kind == FurniKinds.Decor).OrderBy(f => f.TendedAt).ToList();
        if (onShow.Count >= Art.MaxOnShow) place.Furni.Remove(onShow[0]);
        var title = Art.Title(description);
        var wall = place.Spots.FirstOrDefault(s => s.Id == Art.Wall) ?? place.Spots[0];
        var piece = new Furni
        {
            Id = $"piece-{now}", Kind = FurniKinds.Decor, Style = Decor.StyleOf(description),
            Name = title, SpotId = wall.Id, TendedAt = now, OwnerId = participantId,
        };
        place.Furni.Add(piece);
        QueueModel(place, piece, description);
        StepTo(body, place, Art.EaselIn(place)!);
        body.Activity = "at the easel";
        body.ActivityUntil = now + (long)BusyWithFurni.TotalMilliseconds;
        await SaveAsync();
        await PublishAsync(body);
        await PublishPlaceAsync(place);
        return $"finishes {title} {AtName(place, Guid.Empty)}";
    }

    /// <summary>Author: take a generated Place down; anyone there walks home.</summary>
    /// <summary>Their Body, Home and visit history go; guests there go to their own Homes.</summary>
    public async Task RemoveParticipantAsync(Guid participantId)
    {
        await SettleAsync();
        state.State.Bodies.Remove(participantId);
        state.State.Visits.Remove(participantId);
        ReleaseHousemates(state.State.Bodies.Keys.ToHashSet());
        if (OwnHome(participantId) is { } home)
        {
            foreach (var guest in state.State.Bodies.Values.Where(b => (b.DestPlaceId ?? b.PlaceId) == home.Id).ToList())
                if (state.State.Places.HomeOf(guest.ParticipantId) is { } own)
                {
                    guest.PlaceId = own.Id;
                    guest.SpotId = own.Spots[0].Id;
                    guest.DestPlaceId = null;
                    guest.DestSpotId = null;
                    guest.ArriveAt = null;
                    guest.DepartedAt = null;
                    EndPose(guest);
                    await PublishAsync(guest);
                }
            state.State.Places.Remove(home);
        }
        await state.WriteStateAsync();
    }

    public async Task<bool> RemovePlaceAsync(Guid placeId)
    {
        await SettleAsync();
        if (Find(placeId) is not { CustomScene: not null, Kind: PlaceKinds.Public } place) return false;
        foreach (var b in state.State.Bodies.Values.Where(b => (b.DestPlaceId ?? b.PlaceId) == placeId).ToList())
            if (state.State.Places.HomeOf(b.ParticipantId) is { } home)
            {
                b.PlaceId = home.Id;
                b.SpotId = home.Spots[0].Id;
                b.DestPlaceId = null;
                b.DestSpotId = null;
                b.ArriveAt = null;
                b.DepartedAt = null;
                EndPose(b);
                await PublishAsync(b);
            }
        state.State.Places.Remove(place);
        await SaveAsync();
        await PublishPlaceAsync(place);
        return true;
    }
}
