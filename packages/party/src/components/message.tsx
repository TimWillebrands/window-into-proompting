import type { MessageType } from "@proompting/core/types";

const baseClasses =
    "mb-4 p-3 border border-gray-300 shadow-[inset_-1px_-1px_#0a0a0a,inset_1px_1px_#dfdfdf,inset_-2px_-2px_#808080,inset_2px_2px_#c0c0c0] rounded-md";
const userClasses = "ml-6 bg-gradient-to-br from-blue-50 to-blue-100/50";
const aiClasses = "mr-6 bg-gradient-to-br from-green-50 to-green-100/50";

function ChatMessage({
    id,
    message,
    roomId,
    personaName,
}: {
    message: MessageType;
    id: string;
    roomId: string;
    personaName?: string;
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
                personaName={personaName}
                sendAt={message.sendAt ?? undefined}
                roomId={roomId}
            />
            <div className="leading-relaxed text-sm">
                {message.reasoning && (
                    <details>
                        <summary>Reasoning</summary>
                        <div class="window">
                            <div class="title-bar">
                                <div class="title-bar-text">Reasoning</div>
                            </div>
                            <div class="window-body">
                                <streaming-md class="reason-content text-sm">
                                    {message.reasoning}
                                </streaming-md>
                            </div>
                        </div>
                    </details>
                )}
                <streaming-md id="md">{message.message}</streaming-md>
                {message.overseer && (
                    <details className="mt-2">
                        <summary className="text-xs text-gray-500 cursor-pointer">
                            Director's Cut
                        </summary>
                        <div class="p-2 bg-gray-50 rounded text-xs text-gray-600 font-mono whitespace-pre-wrap border border-gray-200">
                            {message.overseer}
                        </div>
                    </details>
                )}
            </div>
        </div>
    );
}

export function MessageHeader({
    personaId,
    roomId,
    sendAt,
    messageId,
    personaName,
}: {
    personaId?: string;
    roomId: string;
    sendAt?: number;
    messageId: string | number;
    personaName?: string;
}) {
    const date = sendAt ? new Date(sendAt) : new Date();
    const timestamp = date.toLocaleString(undefined, {
        month: "short",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit",
    });
    return (
        <div class="flex items-center justify-between gap-2 mb-2 pb-2 border-b border-gray-200">
            <div class="flex items-center gap-2 font-bold text-gray-800 text-sm min-w-0 flex-1">
                {!personaId ? (
                    <span class="flex items-center gap-1">
                        <span class="text-base">👤</span>
                        <span>You</span>
                    </span>
                ) : (
                    <ChatPersonaAvatar
                        personaId={personaId}
                        personaName={personaName}
                    />
                )}
            </div>
            <div class="flex items-center gap-2 flex-shrink-0">
                <span class="font-normal text-xs text-gray-500 hidden sm:inline">
                    {timestamp}
                </span>
                <div class="flex items-center gap-1">
                    <button
                        type="button"
                        class="w-6 h-6 min-w-6! flex items-center justify-center rounded hover:bg-blue-100 transition-colors text-sm opacity-60 hover:opacity-100"
                        title="Re-prompt from here"
                        hx-post={`/party/${roomId}/reprompt/${messageId}`}
                        hx-include="[name='model'], [name='personaId']"
                        hx-swap="none"
                        hx-trigger="click"
                        hx-confirm="Reset conversation from this message and regenerate?"
                    >
                        🔄
                    </button>
                    <button
                        type="button"
                        class="w-6 h-6 min-w-6! flex items-center justify-center rounded hover:bg-red-100 transition-colors text-sm opacity-60 hover:opacity-100"
                        title="Delete message"
                        hx-delete={`/party/${roomId}/messages/${messageId}`}
                        hx-target="closest .message"
                        hx-swap="delete"
                    >
                        🗑️
                    </button>
                </div>
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
    personaName,
}: {
    message: MessageType | number;
    roomId: string;
    personaName?: string;
}) {
    if (typeof message === "object") {
        // Static message display
        return (
            <ChatMessage
                id={`message_${message.messageid}_${roomId}`}
                message={message}
                roomId={roomId}
                personaName={personaName}
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
            x-init={`
                $el.scrollIntoView();
                const followUpPanel = document.getElementById('follow-up-panel');
                if (followUpPanel) followUpPanel.innerHTML = '<div class="text-sm text-gray-500 p-2">Waiting for response...</div>';
                $el.addEventListener('htmx:sseClose', () => {
                    console.log('sse close!')
                    $el.querySelectorAll('streaming-md').forEach(el => el.finish())
                })`}
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

            <streaming-md
                sse-swap="overseer"
                hx-target="#follow-up-panel"
                hx-swap="beforeend"
                class="overseer-content text-sm hidden"
            ></streaming-md>
        </article>
    );
}

export function ChatPersonaAvatar({
    personaId,
    personaName,
    attrs,
}: {
    personaId: string;
    personaName?: string;
    attrs?: Record<string, string>;
}) {
    return (
        <div class="flex" {...attrs}>
            <img
                src={`https://robohash.org/${personaId}.png?size=16x16`}
                alt="avatar"
            />
            &nbsp;
            {personaName ? (
                <span>{personaName}</span>
            ) : (
                <span
                    hx-get={`/personas/${personaId}/avatar`}
                    hx-trigger="load"
                    hx-target="this"
                    hx-swap="outerHTML"
                    hx-params="none"
                >
                    {personaId}
                </span>
            )}
        </div>
    );
}
