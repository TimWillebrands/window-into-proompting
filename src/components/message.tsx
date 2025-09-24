import type { PropsWithChildren } from "hono/jsx";
import type { MessageType } from "@/durable_objects/party";

interface ChatMessageProps {
    isUser: boolean;
    timestamp?: string;
    className?: string;
    [hxAttr: string]: unknown; // For HTMX attributes
}

function ChatMessage({
    isUser,
    children,
    timestamp,
    className = "",
    ...hxAttributes
}: PropsWithChildren<ChatMessageProps>) {
    const baseClasses =
        "mb-4 p-3 border border-gray-300 shadow-[inset_-1px_-1px_#0a0a0a,inset_1px_1px_#dfdfdf,inset_-2px_-2px_#808080,inset_2px_2px_#c0c0c0]";
    const userClasses = "ml-5 bg-blue-50";
    const aiClasses = "mr-5 bg-green-50";

    return (
        <div
            className={`${baseClasses} ${isUser ? userClasses : aiClasses} ${className}`}
            {...hxAttributes}
        >
            <div className="font-bold mb-1 text-blue-800">
                {isUser ? "👤 Human" : "🤖 AI Assistant"}
                {timestamp && (
                    <span className="float-right font-normal text-[10px] text-gray-500">
                        {timestamp}
                    </span>
                )}
            </div>
            <div className="leading-relaxed whitespace-pre-wrap">
                {children}
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
            role="tabpanel"
            hx-ext="sse"
            sse-connect={`/party/${roomId}/messages/${message}`}
            sse-swap="message"
            hx-swap="beforeend"
            hx-target="find .message-content"
            sse-close="finished"
            className="mb-4 mr-5 p-3 border border-gray-300 bg-green-50 shadow-[inset_-1px_-1px_#0a0a0a,inset_1px_1px_#dfdfdf,inset_-2px_-2px_#808080,inset_2px_2px_#c0c0c0]"
        >
            <div className="font-bold mb-2 text-blue-800">
                🤖 AI Assistant
                <span className="float-right font-normal text-[10px] text-gray-500">
                    {new Date().toLocaleTimeString()}
                </span>
            </div>

            {/*<div className="font-sans text-[11px] leading-relaxed min-h-4">
                <TypingIndicator />
            </div>*/}

            <zero-md>
                <template></template>
                <script class="message-content" type="text/markdown"></script>
            </zero-md>
        </article>
    );
}

// Simplified UserMessage component
function UserMessage({ content }: { content: string }) {
    return (
        <ChatMessage
            isUser={true}
            timestamp={new Date().toLocaleTimeString()}
            className="message user-message"
            x-init="$el.closest('.chat-messages').querySelector('.welcome-message').remove()"
        >
            {content}
        </ChatMessage>
    );
}
