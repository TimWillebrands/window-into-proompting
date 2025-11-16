import type { MessageType } from "@/durable_objects/party";

function ChatMessage({
    id,
    message,
    roomId,
}: {
    message: MessageType;
    id: string;
    roomId: string;
}) {
    const isUser = message.senderType === "user";
    const timestamp = new Date(message.sendAt ?? 0).toISOString();
    const baseClasses =
        "mb-4 p-3 border border-gray-300 shadow-[inset_-1px_-1px_#0a0a0a,inset_1px_1px_#dfdfdf,inset_-2px_-2px_#808080,inset_2px_2px_#c0c0c0] rounded-md";
    const userClasses = "ml-6 bg-gradient-to-br from-blue-50 to-blue-100/50";
    const aiClasses = "mr-6 bg-gradient-to-br from-green-50 to-green-100/50";

    return (
        <div
            id={id}
            className={`message ${baseClasses} ${isUser ? userClasses : aiClasses}`}
            x-init="$el.scrollIntoView()"
        >
            <div className="font-bold mb-2 text-gray-800 text-sm">
                {isUser ? (
                    "👤 You"
                ) : (
                    <ChatPersonaAvatar
                        personaId={message.senderId}
                        roomId={roomId}
                    />
                )}
                {timestamp && (
                    <span className="float-right font-normal text-xs text-gray-500">
                        {timestamp}
                    </span>
                )}
                <button
                    type="button"
                    class="float-right w-2"
                    hx-delete={`/party/${roomId}/messages/${message.messageid}`}
                    hx-target="closest .message"
                    hx-swap="delete"
                >
                    🗑
                </button>
            </div>
            <div className="leading-relaxed text-sm">
                <streaming-md id="md">{message.message}</streaming-md>
            </div>
        </div>
    );
}

/**
 * Container for a single message, loads in a response and renders that markdown using a web-component.
 */
export function Message({
    message,
    roomId,
}: {
    message: MessageType | number;
    roomId: string;
}) {
    if (typeof message === "object") {
        // Static message display
        return (
            <ChatMessage
                id={`message_${message.messageid}_${roomId}`}
                message={message}
                roomId={roomId}
            />
        );
    }

    // Streaming AI response message
    return (
        <article
            id={`message_${message}_${roomId}`}
            role="tabpanel"
            hx-ext="sse"
            sse-connect={`/party/${roomId}/messages/${message}`}
            sse-swap="message"
            hx-swap="beforeend"
            hx-target="find .message-content"
            sse-close="finished"
            hx-on--after-swap="this.querySelector('.thinking')?.remove()"
            x-init="$el.scrollIntoView()"
            className="mb-4 mr-6 p-3 border border-gray-300 bg-gradient-to-br from-green-50 to-green-100/50 shadow-[inset_-1px_-1px_#0a0a0a,inset_1px_1px_#dfdfdf,inset_-2px_-2px_#808080,inset_2px_2px_#c0c0c0] rounded-md"
        >
            <div className="font-bold mb-2 text-gray-800 text-sm">
                🤖 AI Assistant
                <span className="float-right font-normal text-xs text-gray-500">
                    {new Date().toLocaleTimeString()}
                </span>
            </div>

            <div
                className="thinking text-sm text-gray-600"
                x-data="{time: 0}"
                x-init="setInterval(() => time++, 1000)"
            >
                💭 Thinking <span x-text="time"> </span> seconds...
                <progress></progress>
            </div>

            <streaming-md className="message-content text-sm"></streaming-md>
        </article>
    );
}

function ChatPersonaAvatar({
    personaId,
    roomId,
}: {
    personaId: string;
    roomId: string;
}) {
    return (
        <div class="flex">
            <img
                src={`https://robohash.org/${personaId}.png?size=16x16`}
                alt="avatar"
            />
            &nbsp;
            <span
                hx-get={`/personas/${personaId}/avatar`}
                hx-trigger="load"
                hx-target="this"
                hx-swap="outerHTML"
                hx-params="none"
            >
                {personaId}
            </span>
        </div>
    );
}
