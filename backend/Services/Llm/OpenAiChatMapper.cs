using System.Text.Json.Nodes;
using OpenAI.Chat;

namespace PartyTown.Services.Llm;

internal static class OpenAiChatMapper
{
    public static List<ChatMessage> ToChatMessages(IReadOnlyList<LlmChatMessage> messages)
    {
        var result = new List<ChatMessage>(messages.Count);

        foreach (var message in messages)
        {
            ChatMessage mapped = message.Role switch
            {
                "system" => new SystemChatMessage(message.Content)
                {
                    ParticipantName = message.Name
                },
                "assistant" => new AssistantChatMessage(message.Content)
                {
                    ParticipantName = message.Name
                },
                _ => new UserChatMessage(message.Content)
                {
                    ParticipantName = message.Name
                }
            };

            result.Add(mapped);
        }

        return result;
    }

    public static ChatCompletionOptions ToChatCompletionOptions(LlmGenerationParams parameters)
    {
        var options = new ChatCompletionOptions
        {
            EndUserId = parameters.UserId,
            Temperature = parameters.Temperature is null ? null : (float)parameters.Temperature.Value
        };

        if (TryGetJsonSchemaResponseFormat(parameters.ResponseFormat, out var responseFormat))
        {
            options.ResponseFormat = responseFormat;
        }

        return options;
    }

    private static bool TryGetJsonSchemaResponseFormat(JsonObject? responseFormatNode, out ChatResponseFormat responseFormat)
    {
        responseFormat = null!;
        if (responseFormatNode is null)
        {
            return false;
        }

        var formatType = responseFormatNode["type"]?.GetValue<string>();
        if (!string.Equals(formatType, "json_schema", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var schemaRoot = responseFormatNode["json_schema"] as JsonObject;
        if (schemaRoot is null)
        {
            return false;
        }

        var name = schemaRoot["name"]?.GetValue<string>();
        var schema = schemaRoot["schema"];
        if (string.IsNullOrWhiteSpace(name) || schema is null)
        {
            return false;
        }

        var strict = schemaRoot["strict"]?.GetValue<bool>();

        responseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
            name,
            BinaryData.FromString(schema.ToJsonString()),
            jsonSchemaIsStrict: strict);

        return true;
    }
}
