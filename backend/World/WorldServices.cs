using System.Text.Json.Nodes;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;

namespace PartyTown.World;

/// <summary>Voicing seam for World Apps (System Two): one-shot text or JSON in a persona's voice.</summary>
public interface IWorldLlm
{
    Task<string?> CompleteAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null);

    /// <param name="fields">String fields the object must have; sent as a JSON schema so small/local models are constrained.</param>
    Task<JsonObject?> CompleteJsonAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null, IReadOnlyList<string>? fields = null);
}

/// <summary>One-shot LLM calls for World Apps: tier routing with General fallback + JSON repair.</summary>
public sealed class WorldLlm(IGrainFactory grains, ILogger<WorldLlm> logger) : IWorldLlm
{
    public Task<string?> CompleteAsync(
        JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null)
        => CompleteCoreAsync(preferred, system, user, ct, temperature, null);

    private async Task<string?> CompleteCoreAsync(
        JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature, string? responseFormat)
    {
        var (endpoint, complexity) = await new LlmCall(grains.GetGrain<ILlmRouterGrain>(0))
            .RouteAsync(preferred == JobComplexity.General ? LlmTiers.General : new[] { preferred, JobComplexity.General }, ct);

        var job = new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = user },
            },
            JobComplexity = complexity,
            ModelParameters = temperature is null ? null : new LlmModelParameters { Temperature = temperature },
            ResponseFormat = responseFormat,
        };
        return await endpoint.CompleteOneShotAsync(job, ct);
    }

    public async Task<JsonObject?> CompleteJsonAsync(
        JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null, IReadOnlyList<string>? fields = null)
    {
        var raw = await CompleteCoreAsync(preferred, system, user, ct, temperature, fields is null ? null : SchemaFor(fields));
        var json = LlmJson.TryRepairObject(raw ?? string.Empty);
        if (json is null)
        {
            logger.LogWarning("World LLM returned unparseable JSON: {Preview}", LlmJson.LogPreview(raw));
            return null;
        }
        try
        {
            return ParseLastWins(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "World LLM JSON parse failed: {Preview}", LlmJson.LogPreview(raw));
            return null;
        }
    }

    /// <summary>OpenAI-style json_schema response format: an object of required string fields.</summary>
    public static string SchemaFor(IReadOnlyList<string> fields)
    {
        var props = new JsonObject();
        foreach (var f in fields) props[f] = new JsonObject { ["type"] = "string" };
        return new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "world",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = props,
                    ["required"] = new JsonArray(fields.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()),
                    ["additionalProperties"] = false,
                },
            },
        }.ToJsonString();
    }

    /// <summary>Models sometimes repeat a key; JsonObject throws on that, so rebuild it last-wins.</summary>
    public static JsonObject? ParseLastWins(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        var obj = new JsonObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
            obj[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        return obj;
    }

    /// <summary>Who the Persona is, for prompts outside the Room pipeline.</summary>
    public static string Identity(CastMember self)
    {
        var parts = new List<string> { $"You are {self.Name}.", Services.ResponsePipeline.LivedSelf.SheetFraming };
        if (!string.IsNullOrWhiteSpace(self.SystemPrompt)) parts.Add(self.SystemPrompt!.Trim());
        if (!string.IsNullOrWhiteSpace(self.Bio)) parts.Add($"About you: {self.Bio!.Trim()}");
        return string.Join("\n", parts);
    }
}

/// <summary>Automatic Capture + rest for World Apps (ADR 0018): moments close, memories form.</summary>
public sealed class WorldMemory(
    IMemoryRepository repository,
    ConsolidationService consolidation,
    IGrainFactory grains,
    ILogger<WorldMemory> logger,
    Self.MomentAppraisal? appraisal = null)
{
    /// <summary>The one Capture entry: capture, then gauge whoever's pile grew. Null on failure.</summary>
    public async Task<MemoryCaptureResult?> CaptureAsync(Moment moment, CancellationToken ct)
    {
        var (partyId, threadId) = (moment.PartyId, moment.ThreadId);
        try
        {
            var result = await repository.CaptureAsync(moment, ct);
            logger.LogInformation(
                "{Source} capture in {ThreadId}@{Mark}: event={Event} recollections={Count}",
                moment.Source, threadId, moment.MarkIndex, result.EventCreated, result.RecollectionsCreated);

            // Conversations move moods (ADR 0021): whoever took something away gets appraised, detached from capture.
            if (appraisal is not null && result.EventCreated && result.RecollectionPersonaIds.Count > 0)
            {
                var who = result.RecollectionPersonaIds.ToHashSet();
                _ = Task.Run(async () =>
                {
                    try { await appraisal.AppraiseAsync(moment, who); }
                    catch (Exception ex) { logger.LogDebug(ex, "Appraisal failed for {ThreadId}", threadId); }
                });
            }

            if (result.RecollectionPersonaIds.Count > 0)
            {
                var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
                var grew = result.RecollectionPersonaIds.ToHashSet();
                consolidation.TriggerGaugedRuns(partyId,
                    cast.Where(c => grew.Contains(c.Id) && c.DefaultDriver == DriverKind.LLM)
                        .Select(c => new ConsolidationSubject(c.Id, c.Name, c.Bio)).ToList(),
                    cast.Select(c => new ConsolidationRosterEntry(c.Id, c.Name)).ToList());
            }
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Source} capture failed in {ThreadId}", moment.Source, threadId);
            return null;
        }
    }

    public async Task<ConsolidationRunResult?> RestAsync(Guid partyId, CastMember self, IReadOnlyList<CastMember> cast, CancellationToken ct)
    {
        try
        {
            return await consolidation.RunAsync(partyId,
                new ConsolidationSubject(self.Id, self.Name, self.Bio),
                cast.Select(c => new ConsolidationRosterEntry(c.Id, c.Name)).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Rest consolidation failed for {Persona}", self.Name);
            return null;
        }
    }

    public async Task<(IReadOnlyList<RecalledMemory> Memories, IReadOnlyList<string> Stances)> RecallAsync(
        Guid partyId, Guid personaId, IReadOnlyList<Guid> anchors, string anchorText, CancellationToken ct)
    {
        Guid? placeId = null;
        try { placeId = await grains.GetGrain<Places.IPlacesGrain>(partyId).GetPlaceOfAsync(personaId); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Place lookup failed for {PersonaId}", personaId); }

        var beat = await repository.RecallBeatAsync(
            new BeatAnchors(personaId, partyId, anchors, anchorText, placeId), memoryLimit: 8, stanceLimit: 6, logger, ct);
        try { beat = Self.StaleRecall.Apply(beat, await grains.GetGrain<Self.IWorldVoiceGrain>(partyId).GetStaleWordsAsync(personaId)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogDebug(ex, "Stale recall filter failed"); }
        return (beat.Memories, beat.Stances.Select(Services.ResponsePipeline.StanceBlock.FormatLine).ToList());
    }
}
