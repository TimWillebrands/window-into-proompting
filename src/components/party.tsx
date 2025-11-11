import { createContext, type PropsWithChildren, useContext } from "hono/jsx";
import type { OpenRouterModelsResponse } from "@/openRouter";
import type { PersonaMetadata } from "./personas";
import { WindowContainer } from "./window";

function ChatInput() {
    const { room, openRouter } = useContext(PartyContext);
    return (
        <div class="p-3 border-t-2 border-gray-300 bg-gradient-to-r from-blue-50 to-indigo-50">
            <div class="flex flex-col gap-3">
                <div class="field-row-stacked">
                    <label
                        htmlFor={`prompt_input_${room}`}
                        class="font-bold text-sm"
                    >
                        💬 Your Message:
                    </label>
                    <textarea
                        name="prompt"
                        id={`prompt_input_${room}`}
                        rows={3}
                        required
                        placeholder="Type your message here... (Ctrl+Enter to send)"
                        class="w-full resize-y font-sans text-sm"
                    ></textarea>
                </div>

                <div class="flex flex-row gap-3 items-end justify-between">
                    <div class="field-row items-center">
                        <label
                            htmlFor={`model_${room}`}
                            class="font-bold text-sm mr-2"
                        >
                            🤖 Model:
                        </label>
                        <select
                            name="model"
                            id={`model_${room}`}
                            class="flex-1"
                            hx-on:change="window.currentModel = event.target.value; analytics.trackModelSelected(event.target.value, 'dropdown')"
                        >
                            {openRouter.models.map((model) => (
                                <option
                                    value={
                                        model.endpoint.model_variant_permaslug
                                    }
                                >
                                    {model.short_name}
                                </option>
                            ))}
                        </select>
                    </div>

                    <div class="field-row gap-2">
                        <button
                            type="button"
                            hx-on:click="
                                document.getElementById('prompt-input').value = '';
                                document.getElementById('prompt-input').focus();
                            "
                        >
                            🗑 Clear
                        </button>
                        <button type="submit" class="min-w-[80px]">
                            🚀 Send
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
}

function StatusBar() {
    return (
        <div class="status-bar">
            <p class="status-bar-field">
                🟢 Connected - <span x-text="user?.fullName"></span>
            </p>
            <p class="status-bar-field" id="message-count">
                💬 0 messages
            </p>
            <p class="status-bar-field">🤖 AI Online</p>
        </div>
    );
}

interface PartyProps {
    room: string;
    openRouter: OpenRouterModelsResponse["data"];
    personaParticipants: PersonaMetadata[];
}

const PartyContext = createContext<PartyProps>({
    room: "",
    openRouter: null as unknown as OpenRouterModelsResponse["data"],
    personaParticipants: [],
});

/**
 * Container for a single party, which manages the layout and behavior of the party window.
 */
export function Party({ room, openRouter, personaParticipants }: PartyProps) {
    return (
        <PartyContext.Provider
            value={{ room, openRouter, personaParticipants }}
        >
            <div x-data={`{ room: "${room}" }`}>
                <WindowContainer
                    id={room}
                    title={`🎉 Party Chat - ${room}`}
                    url={`/party/${room}`}
                >
                    <ChatForm room={room}>
                        <ChatMessagesArea
                            room={room}
                            personaParticipants={personaParticipants}
                        />
                        <StatusBar />
                    </ChatForm>
                </WindowContainer>
            </div>
        </PartyContext.Provider>
    );
}

function ChatMessagesArea({
    room,
    personaParticipants,
}: {
    room: string;
    personaParticipants: PersonaMetadata[];
}) {
    return (
        <div class="window-body overflow-y-auto flex-1 flex flex-col p-0">
            <section class="flex flex-row flex-1 overflow-y-auto overflow-x-hidden">
                <div
                    id="chat-messages"
                    hx-ext="sse"
                    sse-connect={`/party/${room}/messages`}
                    sse-swap="message"
                    hx-swap="beforeend"
                    class="flex-1 grow overflow-y-auto overflow-x-hidden p-4 border-2
                    border-gray-300 m-2 bg-gradient-to-b from-white to-gray-50 font-sans
                    text-sm min-h-[200px] chat-messages
                    shadow-[inset_-1px_-1px_#ffffff,inset_1px_1px_#808080]"
                >
                    <WelcomeMessage />
                </div>
                <menu
                    class="w-64 overflow-y-auto overflow-x-hidden p-2 border-2
                    border-gray-300 m-2 bg-gradient-to-b from-white to-gray-50 font-sans
                    text-sm min-h-[200px] chat-messages
                    shadow-[inset_-1px_-1px_#ffffff,inset_1px_1px_#808080]"
                >
                    <fieldset class="min-w-0">
                        <legend>Persona</legend>
                        <div class="flex flex-col">
                            {personaParticipants.map((participant) => (
                                <PersonaListItem
                                    key={participant.id}
                                    id={participant.id}
                                    name={participant.name}
                                />
                            ))}
                        </div>
                    </fieldset>
                </menu>
            </section>
            <ChatInput />
        </div>
    );
}

function WelcomeMessage() {
    return (
        <div
            id="welcome-message"
            class="text-center text-gray-600 my-6 p-6 border-2 border-dashed
            border-gray-300 rounded-lg bg-gradient-to-br from-blue-50 to-indigo-50
            welcome-message"
        >
            <div class="text-4xl mb-3">💭</div>
            <div class="font-bold text-base">Welcome to Party Chat!</div>
            <div class="text-sm mt-2 text-gray-500">
                Start a conversation with the AI assistant...
            </div>
        </div>
    );
}

function PersonaListItem(persona: PersonaMetadata) {
    return (
        <label htmlFor={persona.id} class="block group cursor-pointer mb-2">
            <input
                type="radio"
                id={persona.id}
                name="personaId"
                value={persona.id}
                class="sr-only"
            />
            <div
                class="
                    w-full
                    p-1 rounded-md transition-colors duration-150
                    bg-slate-100
                    border-2 border-solid
                    border-t-white border-l-white
                    border-r-slate-400 border-b-slate-400
                    shadow-[1px_1px_2px_rgba(0,0,0,0.4)]

                    group-has-[:checked]:bg-blue-600
                    group-has-[:checked]:border-t-slate-600 group-has-[:checked]:border-l-slate-600
                    group-has-[:checked]:border-r-slate-300 group-has-[:checked]:border-b-slate-300
                    group-has-[:checked]:shadow-none
                "
            >
                <div
                    class="flex items-center space-x-2 transition-transform duration-150
                    group-has-[:checked]:translate-x-px group-has-[:checked]:translate-y-px"
                >
                    <div class="sunken-panel p-0.5 flex-shrink-0">
                        <img
                            src={`https://robohash.org/${persona.id}.png?size=48x48`}
                            alt={`${persona.name} avatar`}
                            class="w-12 h-12"
                        />
                    </div>
                    <div class="flex-1 min-w-0">
                        <h4 class="font-bold !text-sm text-black truncate group-has-[:checked]:text-white">
                            {persona.name}
                        </h4>
                        <p class="text-xs text-slate-600 truncate group-has-[:checked]:text-slate-200">
                            ID: {persona.id.slice(0, 24)}...
                        </p>
                    </div>
                </div>
            </div>
        </label>
    );
}

function ChatForm({ room, children }: PropsWithChildren<{ room: string }>) {
    return (
        <form
            hx-post={`/party/${room}/prompt`}
            hx-swap="none"
            hx-include="[name='prompt']"
            hx-trigger="submit, keydown[ctrlKey&&key=='Enter']"
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
            class="flex flex-col h-full"
        >
            {children}
        </form>
    );
}
