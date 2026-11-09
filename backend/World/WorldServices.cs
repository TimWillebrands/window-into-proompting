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
    /// <param name="flags">Boolean fields the object must have, same schema.</param>
    /// <param name="refuse">Why a parsed answer is unusable (null when fine); a refused answer is asked of the next provider.</param>
    Task<JsonObject?> CompleteJsonAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null,
        IReadOnlyList<string>? fields = null, IReadOnlyList<string>? flags = null, Func<JsonObject, string?>? refuse = null);

    /// <summary>Calls made on this persona's behalf: their assigned providers win over the shared pool.</summary>
    IWorldLlm For(Guid personaId) => this;
}

/// <summary>One-shot LLM calls for World Apps: tier routing, provider fallback on a failure or a refused answer, JSON repair.</summary>
public sealed class WorldLlm(IGrainFactory grains, ILogger<WorldLlm> logger) : IWorldLlm
{
    /// <summary>Providers asked per call before giving up: the assigned one, then the pool.</summary>
    private const int MaxProviders = 3;

    public IWorldLlm For(Guid personaId) => new Acting(this, personaId);

    public Task<string?> CompleteAsync(
        JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null)
        => CompleteTextAsync(null, preferred, system, user, ct, temperature);

    public Task<JsonObject?> CompleteJsonAsync(
        JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null,
        IReadOnlyList<string>? fields = null, IReadOnlyList<string>? flags = null, Func<JsonObject, string?>? refuse = null)
        => CompleteJsonCoreAsync(null, preferred, system, user, ct, temperature, fields, flags, refuse);

    private sealed class Acting(WorldLlm llm, Guid persona) : IWorldLlm
    {
        public IWorldLlm For(Guid personaId) => llm.For(personaId);

        public Task<string?> CompleteAsync(
            JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null)
            => llm.CompleteTextAsync(persona, preferred, system, user, ct, temperature);

        public Task<JsonObject?> CompleteJsonAsync(
            JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null,
            IReadOnlyList<string>? fields = null, IReadOnlyList<string>? flags = null, Func<JsonObject, string?>? refuse = null)
            => llm.CompleteJsonCoreAsync(persona, preferred, system, user, ct, temperature, fields, flags, refuse);
    }

    /// <summary>Ask providers in routing order until one answers acceptably; a throw or a refusal moves on to the next.
    /// Null when every answer was refused; rethrows when none answered at all.</summary>
    private async Task<T?> AskAsync<T>(
        Guid? persona, JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature,
        string? responseFormat, Func<string, T?> accept) where T : class
    {
        var call = new LlmCall(grains.GetGrain<ILlmRouterGrain>(0), logger, persona);
        var tiers = preferred == JobComplexity.General ? LlmTiers.General : new[] { preferred, JobComplexity.General };
        var tried = new HashSet<ILlmEndpointGrain>();
        Exception? failure = null;
        var answered = false;
        while (tried.Count < MaxProviders)
        {
            ILlmEndpointGrain endpoint;
            JobComplexity served;
            try
            {
                (endpoint, served) = await call.RouteAsync(tiers, ct, tried);
            }
            catch (InvalidOperationException) when (tried.Count > 0)
            {
                break;
            }
            tried.Add(endpoint);

            var job = new LlmGenerationJob
            {
                Messages = new List<LlmChatMessage>
                {
                    new() { Role = "system", Content = system },
                    new() { Role = "user", Content = user },
                },
                JobComplexity = served,
                ModelParameters = temperature is null ? null : new LlmModelParameters { Temperature = temperature },
                ResponseFormat = responseFormat,
            };
            string raw;
            try
            {
                raw = await endpoint.CompleteOneShotAsync(job, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "World LLM provider failed; trying the next one");
                failure = ex;
                continue;
            }
            answered = true;
            if (accept(raw) is { } ok) return ok;
        }
        if (!answered && failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        return null;
    }

    /// <summary>Plain text; an error placeholder in place of an answer is no answer.</summary>
    private Task<string?> CompleteTextAsync(
        Guid? persona, JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature)
        => AskAsync(persona, preferred, system, user, ct, temperature, null, raw =>
        {
            if (!Services.ResponsePipeline.SpeechCleanup.IsFailureMarker(raw)) return raw;
            logger.LogWarning("World LLM answered with a failure placeholder: {Preview}", LlmJson.LogPreview(raw));
            return null;
        });

    /// <summary>A string field holding an error placeholder ("[ERROR: Can't parse response]"); null when none does.</summary>
    public static string? FailedField(JsonObject json, IReadOnlyList<string>? fields)
        => (fields ?? json.Select(kv => kv.Key).ToList())
            .FirstOrDefault(f => json[f] is JsonValue v && v.TryGetValue<string>(out var s) && Services.ResponsePipeline.SpeechCleanup.IsFailureMarker(s));

    /// <summary>A field the schema requires that the answer left out; null when all are there.</summary>
    public static string? MissingField(JsonObject json, IReadOnlyList<string>? fields, IReadOnlyList<string>? flags)
        => (fields ?? Array.Empty<string>()).Concat(flags ?? Array.Empty<string>()).FirstOrDefault(f => json[f] is null);

    private Task<JsonObject?> CompleteJsonCoreAsync(
        Guid? persona, JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature,
        IReadOnlyList<string>? fields, IReadOnlyList<string>? flags, Func<JsonObject, string?>? refuse)
        => AskAsync(persona, preferred, system, user, ct, temperature,
            fields is null && flags is null ? null : SchemaFor(fields ?? Array.Empty<string>(), flags), raw =>
            {
                var json = LlmJson.TryRepairObject(raw ?? string.Empty);
                if (json is null)
                {
                    logger.LogWarning("World LLM returned unparseable JSON: {Preview}", LlmJson.LogPreview(raw));
                    return null;
                }
                JsonObject? parsed;
                try
                {
                    parsed = ParseLastWins(json);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "World LLM JSON parse failed: {Preview}", LlmJson.LogPreview(raw));
                    return null;
                }
                if (parsed is null) return null;
                // A parse that "succeeds" around a placeholder, a gap in the schema or a refused answer is still a failed answer.
                var problem = FailedField(parsed, fields) is { } failed ? $"field {failed} holds a failure placeholder"
                    : MissingField(parsed, fields, flags) is { } missing ? $"field {missing} is missing"
                    : refuse?.Invoke(parsed);
                if (problem is null) return parsed;
                logger.LogWarning("World LLM JSON refused ({Problem}): {Preview}", problem, LlmJson.LogPreview(raw));
                return null;
            });

    /// <summary>OpenAI-style json_schema response format: an object of required string fields and boolean flags.</summary>
    public static string SchemaFor(IReadOnlyList<string> fields, IReadOnlyList<string>? flags = null)
    {
        var props = new JsonObject();
        foreach (var f in fields) props[f] = new JsonObject { ["type"] = "string" };
        foreach (var f in flags ?? Array.Empty<string>()) props[f] = new JsonObject { ["type"] = "boolean" };
        var required = fields.Concat(flags ?? Array.Empty<string>()).Select(f => (JsonNode?)JsonValue.Create(f)).ToArray();
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
                    ["required"] = new JsonArray(required),
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
        // The distilled sheet (ADR 0021) replaces sheet and bio once it exists; the authored ones stay in the library.
        if (Self.LivedSheets.Lived(self.Id, self.SystemPrompt) is { } lived) return string.Join("\n", parts.Append(lived));
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

            // What was talked about is footfall on the Locales it names (ADR 0027), credited to who spoke.
            if (result.Concepts.Count > 0)
                _ = grains.GetGrain<Places.IPlacesGrain>(partyId).NoteTopicsAsync(
                    result.Concepts.ToList(), moment.Lines.Select(l => l.SenderId).Distinct().ToList());

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

        var beat = await Self.PersonaRecall.ForBeatAsync(repository, grains,
            new BeatAnchors(personaId, partyId, anchors, anchorText, placeId), memoryLimit: 4, stanceLimit: 6, logger, ct);
        return (beat.Memories, beat.Stances.Select(Services.ResponsePipeline.StanceBlock.FormatLine).ToList());
    }
}
