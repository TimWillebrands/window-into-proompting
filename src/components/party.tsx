import { createContext, useContext } from "hono/jsx";
import { WindowContainer } from "./window";

function ChatInput() {
    const { room } = useContext(PartyContext);
    return (
        <div className="p-2 border-t border-gray-300">
            <form
                hx-post={`/party/${room}/message`}
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
                        hx-post={`/party/${room}/message`}
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

interface PartyProps {
    room: string;
}

const PartyContext = createContext({ room: "" });

/**
 * Container for a single party, which manages the layout and behavior of the party window.
 */
export function Party({ room }: PartyProps) {
    return (
        <PartyContext.Provider value={{ room }}>
            <div x-data={`{ room: "${room}" }`}>
                <WindowContainer id={room} title={`🎉 Party Chat - ${room}`}>
                    <ChatMessagesArea />
                    <StatusBar />
                </WindowContainer>
            </div>
        </PartyContext.Provider>
    );
}
function ChatMessagesArea() {
    return (
        <div className="window-body overflow-y-auto flex-1 flex flex-col p-0">
            <div
                id="chat-messages"
                className="flex-1 overflow-y-auto overflow-x-hidden p-4 border-2
                    border-inset border-gray-300 m-2 bg-white font-sans
                    text-[11px] h-0 min-h-[200px] chat-messages"
            >
                <WelcomeMessage />
            </div>
            <ChatInput />
        </div>
    );
}

function WelcomeMessage() {
    return (
        <div
            className="text-center text-gray-500 my-5 p-5 border border-dashed
            border-gray-300 welcome-message"
        >
            <div className="text-base mb-2">💭</div>
            <div>Welcome to Party Chat!</div>
            <div className="text-[10px] mt-1">
                Start a conversation with the AI...
            </div>
        </div>
    );
}
