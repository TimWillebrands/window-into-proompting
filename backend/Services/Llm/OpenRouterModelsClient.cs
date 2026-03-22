using System.Text.Json;

namespace PartyTown.Services.Llm;

public sealed class OpenRouterModelsClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<LlmModel>> LoadFreeModelsAsync(CancellationToken cancellationToken = default)
    {
        const string url = "https://openrouter.ai/api/frontend/models/find?context=128000&fmt=cards&input_modalities=text&max_price=0&order=top-weekly";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var models = new List<LlmModel>();
        if (!document.RootElement.TryGetProperty("data", out var dataNode))
        {
            return models;
        }

        if (!dataNode.TryGetProperty("models", out var modelsNode) || modelsNode.ValueKind != JsonValueKind.Array)
        {
            return models;
        }

        foreach (var modelNode in modelsNode.EnumerateArray())
        {
            if (!modelNode.TryGetProperty("endpoint", out var endpointNode))
            {
                continue;
            }

            if (!endpointNode.TryGetProperty("model_variant_permaslug", out var idNode))
            {
                continue;
            }

            var id = idNode.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var name = modelNode.TryGetProperty("short_name", out var nameNode)
                ? nameNode.GetString() ?? id
                : id;

            var description = modelNode.TryGetProperty("description", out var descriptionNode)
                ? descriptionNode.GetString()
                : null;

            int? contextLength = null;
            if (modelNode.TryGetProperty("context_length", out var contextNode) && contextNode.TryGetInt32(out var context))
            {
                contextLength = context;
            }

            models.Add(new LlmModel
            {
                Id = id,
                Name = name,
                Provider = "openrouter",
                Description = description,
                ContextLength = contextLength
            });
        }

        return models;
    }
}
