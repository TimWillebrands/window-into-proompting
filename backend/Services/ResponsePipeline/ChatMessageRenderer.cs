using System.Security;
using PartyTown.Model;

namespace PartyTown.Services.ResponsePipeline;

internal static class ChatMessageRenderer
{
    public static string Render(ChatMessage message, string senderName)
        => $"<message sender=\"{SecurityElement.Escape(senderName)}\" senderId=\"{message.SenderId}\">\n{SecurityElement.Escape(message.Content ?? "")}\n</message>";
}
