import { createContext, type PropsWithChildren, useContext } from "hono/jsx";

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
                {isUser ? "👤 You" : "🤖 AI Assistant"}
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

function WelcomeMessage() {
    return (
        <div className="text-center text-gray-500 my-5 p-5 border border-dashed border-gray-300">
            <div className="text-base mb-2">💭</div>
            <div>Welcome to Party Chat!</div>
            <div className="text-[10px] mt-1">
                Start a conversation with the AI...
            </div>
        </div>
    );
}

function TypingIndicator() {
    return (
        <span className="typing-indicator htmx-indicator opacity-60">
            <span className="animate-pulse">●</span>
            <span className="animate-pulse [animation-delay:0.2s]">●</span>
            <span className="animate-pulse [animation-delay:0.4s]">●</span>
        </span>
    );
}

function ChatInput() {
    const { room } = useContext(PartyContext);
    return (
        <div className="p-2 border-t border-gray-300">
            <form
                hx-post={`/${room}/message`}
                hx-target="#chat-messages"
                hx-swap="beforeend"
                hx-include="[name='prompt']"
                hx-on:before-request="
                    event.target.querySelector('[type=submit]').disabled = true;
                    event.target.querySelector('[type=submit]').textContent = '⏳ Sending...';
                "
                hx-on:after-request="
                    event.target.querySelector('[type=submit]').disabled = false;
                    event.target.querySelector('[type=submit]').textContent = '🚀 Send';
                    event.target.reset();
                    document.getElementById('chat-messages').scrollTop = document.getElementById('chat-messages').scrollHeight;
                "
                className="flex flex-col gap-2"
            >
                {/* Hidden div that gets submitted and creates user message */}
                <div hx-swap-oob="beforeend:#chat-messages">
                    <template>
                        <div className="user-message mb-4 ml-5 p-3 border border-gray-300 bg-blue-50 shadow-[inset_-1px_-1px_#0a0a0a,inset_1px_1px_#dfdfdf,inset_-2px_-2px_#808080,inset_2px_2px_#c0c0c0]">
                            <div className="font-bold mb-1 text-blue-800">
                                👤 You
                                <span className="float-right font-normal text-[10px] text-gray-500">
                                    {/* Timestamp will be added by server */}
                                </span>
                            </div>
                            <div className="leading-relaxed whitespace-pre-wrap">
                                {/* Message content will be added by server */}
                            </div>
                        </div>
                    </template>
                </div>

                <div className="field-row-stacked">
                    <label htmlFor="prompt-input" className="font-bold">
                        💬 Your Message:
                    </label>
                    <textarea
                        name="prompt"
                        id="prompt-input"
                        rows={3}
                        required
                        placeholder="Type your message here... (Ctrl+Enter to send)"
                        className="w-full resize-y font-sans text-[11px]"
                        hx-trigger="keydown[ctrlKey&&key=='Enter']"
                        hx-post={`/${room}/message`}
                        hx-target="#chat-messages"
                        hx-swap="beforeend"
                        hx-include="closest form"
                    ></textarea>
                </div>

                <div className="field-row justify-end gap-2">
                    <button
                        type="button"
                        hx-on:click="
                            document.getElementById('prompt-input').value = '';
                            document.getElementById('prompt-input').focus();
                        "
                    >
                        🗑 Clear
                    </button>
                    <button type="submit" className="min-w-[75px]">
                        🚀 Send
                    </button>
                </div>
            </form>
        </div>
    );
}

function StatusBar() {
    return (
        <div className="status-bar">
            <p className="status-bar-field">🟢 Ready to chat</p>
            <p className="status-bar-field" id="message-count">
                Messages: 0
            </p>
            <p className="status-bar-field">🤖 AI Assistant Online</p>
        </div>
    );
}

function WindowContainer({ children }: PropsWithChildren<unknown>) {
    const { room } = useContext(PartyContext);
    return (
        <div
            id={room}
            className="
                window absolute w-[clamp(600px,80vw,1000px)] h-[clamp(400px,70vh,700px)]
                flex flex-col resize overflow-hidden min-w-[500px] min-h-[350px]"
            style="left: calc(50% - clamp(600px,80vw,1000px) / 2); top: calc(50% - clamp(400px,70vh,700px) / 2)"
        >
            <div
                className="title-bar box-content"
                x-on:pointerdown="
                    dragTarget = document.querySelector('#'+room);
                    const rect = dragTarget.getBoundingClientRect();
                    dragTarget.offsetX = $event.clientX - rect.left;
                    dragTarget.offsetY = $event.clientY - rect.top;
                    $event.preventDefault(); "
            >
                <div className="title-bar-text">
                    🎉 Party Chat - {room}{" "}
                    <strong x-text="dragTarget != null"></strong>
                </div>
                <div className="title-bar-controls">
                    <button type="button" aria-label="Minimize"></button>
                    <button type="button" aria-label="Maximize"></button>
                    <button type="button" aria-label="Close"></button>
                </div>
            </div>
            {children}
        </div>
    );
}

function ChatMessagesArea() {
    return (
        <div className="window-body overflow-y-auto flex-1 flex flex-col p-0">
            <div
                id="chat-messages"
                className="flex-1 overflow-y-auto overflow-x-hidden p-4 border-2 border-inset border-gray-300 m-2 bg-white font-sans text-[11px] h-0 min-h-[200px]"
            >
                <WelcomeMessage />
            </div>
            <ChatInput />
        </div>
    );
}

interface PartyProps {
    room: string;
}

const PartyContext = createContext({ room: "" });

/**
 * Container for all parties, which manages the layout and behavior of party windows.
 */
export function PartiesZone({ children }: PropsWithChildren<unknown>) {
    return (
        <section
            x-data="{ dragTarget: null }"
            x-on:pointerup="dragTarget = null"
            x-on:pointermove="if(dragTarget && dragTarget.offsetX !== undefined) {
                const newX = event.clientX - dragTarget.offsetX;
                const newY = event.clientY - dragTarget.offsetY;
                dragTarget.style.left = newX + 'px';
                dragTarget.style.top = newY + 'px';
                dragTarget.style.zIndex = '1000';
            }"
            className="fixed inset-0 w-screen h-screen flex justify-center items-center bg-gradient-to-br from-slate-300 via-slate-400 to-slate-300"
        >
            {children}
        </section>
    );
}

/**
 * Container for a single party, which manages the layout and behavior of the party window.
 */
export function Party({ room }: PartyProps) {
    return (
        <PartyContext.Provider value={{ room }}>
            <div x-data={`{ room: "${room}" }`}>
                <WindowContainer>
                    <ChatMessagesArea />
                    <StatusBar />
                </WindowContainer>
            </div>
        </PartyContext.Provider>
    );
}

/**
 * Container for a single message, loads in a response and renders that markdown using a web-component.
 */
export function Message({
    room,
    content,
    isUser = false,
}: {
    room: string;
    content?: string;
    isUser?: boolean;
}) {
    if (content) {
        // Static message display
        return (
            <ChatMessage
                isUser={isUser}
                timestamp={new Date().toLocaleTimeString()}
                className="message"
                hx-swap-oob="afterbegin:#message-count"
            >
                {content}
            </ChatMessage>
        );
    }

    // Streaming AI response message
    return (
        <article
            role="tabpanel"
            hx-ext="sse"
            sse-connect={`${room}/prompt`}
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

            <div className="font-sans text-[11px] leading-relaxed min-h-4">
                <TypingIndicator />
            </div>

            <zero-md>
                <template></template>
                <script class="message-content" type="text/markdown"></script>
            </zero-md>
        </article>
    );
}

// Simplified UserMessage component
export function UserMessage({ content }: { content: string }) {
    return (
        <ChatMessage
            isUser={true}
            timestamp={new Date().toLocaleTimeString()}
            className="message user-message"
        >
            {content}
        </ChatMessage>
    );
}
