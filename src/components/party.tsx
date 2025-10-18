import { createContext, useContext } from "hono/jsx";
import { models } from "@/durable_objects/party";
import { WindowContainer } from "./window";

function ChatInput() {
    const { room } = useContext(PartyContext);
    return (
        <div className="p-3 border-t-2 border-gray-300 bg-gradient-to-r from-blue-50 to-indigo-50">
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
                    const formData = new FormData(event.target);
                    const prompt = formData.get('prompt')?.toString() || '';
                    const model = formData.get('model')?.toString() || '';
                    analytics.trackMessageSent({
                        party_id: '${room}',
                        message_length: prompt.length,
                        model_selected: model,
                        message_type: 'text'
                    });
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
                        className="w-full resize-y font-sans text-sm"
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
                        <select name="model" id="model" className="flex-1" hx-on:change="analytics.trackModelSelected(event.target.value, 'dropdown')">
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
                            🗑️ Clear
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
            <p className="status-bar-field">🟢 Connected - <span x-text="user?.fullName"></span></p>
            <p className="status-bar-field" id="message-count">
                💬 0 messages
            </p>
            <p className="status-bar-field">🤖 AI Online</p>
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
                    border-gray-300 m-2 bg-gradient-to-b from-white to-gray-50 font-sans
                    text-sm h-0 min-h-[200px] chat-messages
                    shadow-[inset_-1px_-1px_#ffffff,inset_1px_1px_#808080]"
                hx-on-htmx-sse-before-message="console.log('receiving message')"
                hx-on--sse-after-message="console.log('message received', this)"
                hx-on--after-swap="
                    console.log('message swapped');
                    // Track message received when AI responds
                    if (this.textContent && this.textContent.includes('AI:')) {
                        analytics.trackMessageReceived('${room}', this.textContent.length);
                    }
                "
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
            className="text-center text-gray-600 my-6 p-6 border-2 border-dashed
            border-gray-300 rounded-lg bg-gradient-to-br from-blue-50 to-indigo-50 welcome-message"
        >
            <div className="text-4xl mb-3">💭</div>
            <div className="font-bold text-base">Welcome to Party Chat!</div>
            <div className="text-sm mt-2 text-gray-500">
                Start a conversation with the AI assistant...
            </div>
        </div>
    );
}
