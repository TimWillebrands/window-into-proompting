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

    Task<JsonObject?> CompleteJsonAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null);
}

/// <summary>One-shot LLM calls for World Apps: tier routing with General fallback + JSON repair.</summary>
public sealed class WorldLlm(IGrainFactory grains, ILogger<WorldLlm> logger) : IWorldLlm
{
    public async Task<string?> CompleteAsync(
        JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null)
    {
        var router = grains.GetGrain<ILlmRouterGrain>(0);
        ILlmEndpointGrain endpoint;
        var complexity = preferred;
        try
        {
            endpoint = await router.RouteAsync(preferred, ct);
        }
        catch (InvalidOperationException)
        {
            complexity = JobComplexity.General;
            endpoint = await router.RouteAsync(JobComplexity.General, ct);
        }

        var job = new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = user },
            },
            JobComplexity = complexity,
            ModelParameters = temperature is null ? null : new LlmModelParameters { Temperature = temperature },
        };
        return await endpoint.CompleteOneShotAsync(job, ct);
    }

    public async Task<JsonObject?> CompleteJsonAsync(
        JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null)
    {
        var raw = await CompleteAsync(preferred, system, user, ct, temperature);
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
        var parts = new List<string> { $"You are {self.Name}." };
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
    ILogger<WorldMemory> logger)
{
    public async Task<MemoryCaptureResult?> CaptureAsync(
        Guid partyId,
        Guid threadId,
        int anchorMessageId,
        IReadOnlyList<ChatMessage> context,
        IReadOnlyList<ParticipantView> present,
        CancellationToken ct)
    {
        try
        {
            var result = await repository.CaptureMomentAsync(partyId, threadId, anchorMessageId, present, context, ct);
            logger.LogInformation(
                "World capture in {ThreadId}@{MessageId}: event={Event} recollections={Count}",
                threadId, anchorMessageId, result.EventCreated, result.RecollectionsCreated);

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
            logger.LogWarning(ex, "World capture failed in {ThreadId}@{MessageId}", threadId, anchorMessageId);
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
        IReadOnlyList<RecalledMemory> memories = Array.Empty<RecalledMemory>();
        IReadOnlyList<string> stances = Array.Empty<string>();
        try
        {
            memories = await repository.RecallAsync(personaId, partyId, anchors, anchorText, 8, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "World recall failed for {PersonaId}", personaId);
        }
        try
        {
            stances = (await repository.RecallStancesAsync(partyId, personaId, anchors, anchorText, 6, ct))
                .Select(Services.ResponsePipeline.StanceBlock.FormatLine).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "World stance recall failed for {PersonaId}", personaId);
        }
        return (memories, stances);
    }
}
