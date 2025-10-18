import type { PropsWithChildren } from "hono/jsx";
import type { MessageType } from "@/durable_objects/party";

interface ChatMessageProps {
    id: string;
    isUser: boolean;
    timestamp?: string;
    className?: string;
    [hxAttr: string]: unknown; // For HTMX attributes
}

function ChatMessage({
    id,
    isUser,
    children,
    timestamp,
    className = "",
    ...hxAttributes
}: PropsWithChildren<ChatMessageProps>) {
    const baseClasses =
        "mb-4 p-3 border border-gray-300 shadow-[inset_-1px_-1px_#0a0a0a,inset_1px_1px_#dfdfdf,inset_-2px_-2px_#808080,inset_2px_2px_#c0c0c0] rounded-md";
    const userClasses = "ml-6 bg-gradient-to-br from-blue-50 to-blue-100/50";
    const aiClasses = "mr-6 bg-gradient-to-br from-green-50 to-green-100/50";

    return (
        <div
            id={id}
            className={`${baseClasses} ${isUser ? userClasses : aiClasses} ${className}`}
            x-init="$el.scrollIntoView()"
            {...hxAttributes}
        >
            <div className="font-bold mb-2 text-gray-800 text-sm">
                {isUser ? "👤 You" : "🤖 AI Assistant"}
                {timestamp && (
                    <span className="float-right font-normal text-xs text-gray-500">
                        {timestamp}
                    </span>
                )}
            </div>
            <div className="leading-relaxed text-sm">
                <streaming-md id="md">{children}</streaming-md>
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
                isUser={message.sender === "user"}
                timestamp={new Date(message.sendAt ?? 0).toISOString()}
                className="message"
            >
                {message.message}
            </ChatMessage>
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
            {/*<zero-md>
                <template></template>
                <script class="message-content" type="text/markdown"></script>
            </zero-md>*/}
        </article>
    );
}
