import { createContext, useContext } from "hono/jsx";
import { models } from "@/durable_objects/party";
import { WindowContainer } from "./window";

function ChatInput() {
    const { room } = useContext(PartyContext);
    return (
        <div className="p-3 border-t-2 border-inset border-gray-300">
            <form
                hx-post={`/party/${room}/prompt`}
                hx-swap="none"
                hx-include="[name='prompt']"
                hx-on:before-request="
                    event.target.querySelector('[type=submit]').disabled = true;
                    event.target.querySelector('[type=submit]').textContent = '⏳ Sending...'; "
                hx-on:after-request="
                    event.target.querySelector('[type=submit]').disabled = false;
                    event.target.querySelector('[type=submit]').textContent = '🚀 Send';
                    event.target.reset();
                    document.getElementById('chat-messages').scrollTop = document.getElementById('chat-messages').scrollHeight; "
                className="flex flex-col gap-3"
            >
                <div className="field-row-stacked">
                    <label htmlFor="prompt-input" className="font-bold text-sm">
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
                        hx-post={`/party/${room}/prompt`}
                        hx-target="none"
                        hx-include="[name='prompt']"
                    ></textarea>
                </div>

                <div className="flex flex-row gap-3 items-end justify-between">
                    <div className="field-row items-center">
                        <label
                            htmlFor="model"
                            className="font-bold text-sm mr-2"
                        >
                            🤖 Model:
                        </label>
                        <select name="model" id="model" className="flex-1">
                            {models.map((model) => (
                                <option value={model}>{model}</option>
                            ))}
                        </select>
                    </div>

                    <div className="field-row gap-2">
                        <button
                            type="button"
                            hx-on:click="
                                document.getElementById('prompt-input').value = '';
                                document.getElementById('prompt-input').focus();
                            "
                        >
                            🗑 Clear
                        </button>
                        <button type="submit" className="min-w-[80px]">
                            🚀 Send
                        </button>
                    </div>
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
                <WindowContainer
                    id={room}
                    title={`🎉 Party Chat - ${room}`}
                    url={`/party/${room}`}
                >
                    <ChatMessagesArea room={room} />
                    <StatusBar />
                </WindowContainer>
            </div>
        </PartyContext.Provider>
    );
}
function ChatMessagesArea({ room }: { room: string }) {
    return (
        <div className="window-body overflow-y-auto flex-1 flex flex-col p-0">
            <div
                id="chat-messages"
                hx-ext="sse"
                sse-connect={`/party/${room}/messages`}
                sse-swap="message"
                hx-swap="beforeend"
                className="flex-1 overflow-y-auto overflow-x-hidden p-4 border-2
                    border-inset border-gray-300 m-2 bg-white font-sans
                    text-[11px] h-0 min-h-[200px] chat-messages"
                hx-on-htmx-sse-before-message="console.log('receiving message')"
                hx-on--sse-after-message="console.log('message received', this)"
                hx-on--after-swap="console.log('message swapped')"
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
            id="welcome-message"
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
