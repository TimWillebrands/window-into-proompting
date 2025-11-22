import type { MessageType } from "@/durable_objects/party";

const baseClasses =
    "mb-4 p-3 border border-gray-300 shadow-[inset_-1px_-1px_#0a0a0a,inset_1px_1px_#dfdfdf,inset_-2px_-2px_#808080,inset_2px_2px_#c0c0c0] rounded-md";
const userClasses = "ml-6 bg-gradient-to-br from-blue-50 to-blue-100/50";
const aiClasses = "mr-6 bg-gradient-to-br from-green-50 to-green-100/50";

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

    return (
        <div
            id={id}
            class={`message ${baseClasses} ${isUser ? userClasses : aiClasses}`}
            x-init="$el.scrollIntoView()"
        >
            <MessageHeader
                messageId={message.messageid}
                personaId={!isUser ? message.senderId : undefined}
                sendAt={message.sendAt}
                roomId={roomId}
            />
            <div className="leading-relaxed text-sm">
                <streaming-md id="md">{message.message}</streaming-md>
            </div>
        </div>
    );
}

export function MessageHeader({
    personaId,
    roomId,
    sendAt,
    messageId,
}: {
    personaId?: string;
    roomId: string;
    sendAt?: number;
    messageId: string | number;
}) {
    const timestamp = sendAt
        ? new Date(sendAt).toISOString()
        : new Date().toISOString();
    return (
        <div class="font-bold mb-2 text-gray-800 text-sm">
            {!personaId ? (
                "👤 You"
            ) : (
                <ChatPersonaAvatar personaId={personaId} roomId={roomId} />
            )}
            <span className="float-right font-normal text-xs text-gray-500">
                {timestamp ?? new Date().toLocaleString()}
            </span>
            <button
                type="button"
                class="float-right w-2"
                hx-delete={`/party/${roomId}/messages/${messageId}`}
                hx-target="closest .message"
                hx-swap="delete"
            >
                🗑
            </button>
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
            sse-close="finished"
            x-init=" $el.scrollIntoView(); "
            class={`message ${baseClasses} ${aiClasses}`}
        >
            <div
                sse-swap="persona"
                hx-target="this"
                hx-swap="outerHTML"
                className="thinking text-sm text-gray-600"
                x-data="{time: 0}"
                x-init="setInterval(() => time++, 1000)"
            >
                💭 Thinking <span x-text="time"> </span> seconds...
                <progress></progress>
            </div>

            <details>
                <summary>Reasoning</summary>
                <div class="window">
                    <div class="title-bar">
                        <div class="title-bar-text">Reasoning</div>
                    </div>
                    <div class="window-body">
                        <streaming-md
                            sse-swap="reasoning"
                            hx-swap="beforeend"
                            class="reason-content text-sm"
                        ></streaming-md>
                    </div>
                </div>
            </details>

            <streaming-md
                sse-swap="message"
                hx-swap="beforeend"
                class="message-content text-sm"
            ></streaming-md>
        </article>
    );
}

export function ChatPersonaAvatar({
    personaId,
    roomId,
    attrs,
}: {
    personaId: string;
    roomId: string;
    attrs?: Record<string, string>;
}) {
    return (
        <div class="flex" {...attrs}>
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
