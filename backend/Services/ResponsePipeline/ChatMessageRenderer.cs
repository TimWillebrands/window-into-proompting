using System.Security;
using PartyTown.Model;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Shared XML rendering for chat messages handed to the LLM. Both decision and generation
/// prompts render history through this so the model sees one consistent schema.
///
/// NOTE: SecurityElement.Escape handles XML syntax characters only. It is NOT a prompt-injection
/// defense — message content must be treated as untrusted input to the model. If personas gain
/// real capabilities (tool calls, file writes, privileged retrieval), add a moderation pass
/// upstream instead of relying on escaping here.
/// </summary>
internal static class ChatMessageRenderer
{
    public static string Render(ChatMessage message, string senderName)
        => $"<message sender=\"{SecurityElement.Escape(senderName)}\" senderId=\"{message.SenderId}\">\n{SecurityElement.Escape(message.Content ?? "")}\n</message>";
}
