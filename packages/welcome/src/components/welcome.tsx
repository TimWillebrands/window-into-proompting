import { defaultPersonas } from "@proompting/core";
import { WindowContainer } from "@proompting/desktop";

export function Welcome() {
    return (
        <WindowContainer
            id="welcome"
            title="Welcome to Proompt.party"
            url="/welcome"
        >
            <div
                className="window-body flex flex-col flex-1 min-h-0 p-0 bg-[#ECE9D8]"
                x-data="{ 
                    currentStep: $persist(1),
                    stepStartTime: Date.now(),
                    tourStarted: false
                }"
                x-init="
                    if (!tourStarted) {
                        analytics.trackWelcomeTourStarted();
                        tourStarted = true;
                    }
                "
                x-effect="
                    if (currentStep !== 1) {
                        const timeSpent = Date.now() - stepStartTime;
                        analytics.trackWelcomeStepCompleted({
                            step_number: currentStep - 1,
                            step_name: getStepName(currentStep - 1),
                            time_spent: timeSpent
                        });
                        stepStartTime = Date.now();
                    }
                    
                    function getStepName(step) {
                        const names = {
                            1: 'Introduction & Login',
                            2: 'Create Personas',
                            3: 'Start Party Chats',
                            4: 'Getting Started'
                        };
                        return names[step] || 'Unknown';
                    }
                "
            >
                {/* Content Area */}
                <div className="flex-1 min-h-0 overflow-y-auto p-6">
                    {/* Step 1: Introduction & Login */}
                    <div x-show="currentStep === 1" className="space-y-4">
                        <div className="flex items-start gap-4 mb-6">
                            <div
                                className="text-7xl leading-none select-none"
                                style="filter: drop-shadow(2px 2px 0px rgba(0,0,0,0.2))"
                            >
                                🎪
                            </div>
                            <div className="flex-1">
                                <h1
                                    className="text-2xl font-bold text-[#003399] mb-2"
                                    style="text-shadow: 1px 1px 0px rgba(255,255,255,0.5)"
                                >
                                    Welcome to Proompt.party
                                </h1>
                                <p className="text-sm leading-relaxed text-gray-800">
                                    A multiplayer chat application where you can
                                    have conversations with other users and AI
                                    personas with distinct personalities.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar bg-gradient-to-r from-[#0054E3] to-[#1084D0]">
                                <div className="title-bar-text text-white font-bold">
                                    Getting Started
                                </div>
                            </div>
                            <div className="window-body p-4 space-y-3">
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">
                                        1.
                                    </span>
                                    <p className="text-sm text-gray-800">
                                        <strong>Log in:</strong> Click the user
                                        button in the taskbar at the bottom of
                                        the screen to sign in or create an
                                        account.
                                    </p>
                                </div>
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">
                                        2.
                                    </span>
                                    <p className="text-sm text-gray-800">
                                        <strong>Create personas:</strong> Define
                                        system prompts that give AI agents their
                                        personality and behavior.
                                    </p>
                                </div>
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">
                                        3.
                                    </span>
                                    <p className="text-sm text-gray-800">
                                        <strong>Start party chats:</strong>{" "}
                                        Create or join chat rooms to talk with
                                        other users and your AI personas.
                                    </p>
                                </div>
                            </div>
                        </div>
                    </div>

                    {/* Step 2: Personas Feature */}
                    <div
                        x-show="currentStep === 2"
                        className="space-y-4"
                        x-cloak
                    >
                        <div className="flex items-start gap-4 mb-4">
                            <div
                                className="text-6xl leading-none select-none"
                                style="filter: drop-shadow(2px 2px 0px rgba(0,0,0,0.2))"
                            >
                                🎭
                            </div>
                            <div className="flex-1">
                                <h1
                                    className="text-2xl font-bold text-[#003399] mb-2"
                                    style="text-shadow: 1px 1px 0px rgba(255,255,255,0.5)"
                                >
                                    Create Personas
                                </h1>
                                <p className="text-sm leading-relaxed text-gray-800">
                                    Personas are system prompts that define how
                                    AI agents behave in conversations. Create
                                    and manage different personas to give your
                                    AI agents distinct personalities.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar">
                                <div className="title-bar-text">
                                    Example Persona
                                </div>
                            </div>
                            <div className="window-body p-4 space-y-3">
                                <p className="text-xs text-gray-700 mb-2">
                                    Each persona has a system prompt that
                                    determines its behavior, tone, and
                                    personality. Here's an example:
                                </p>
                                <div className="field-row-stacked">
                                    <label className="font-bold text-xs">
                                        SYSTEM PROMPT
                                    </label>
                                    <textarea
                                        rows={4}
                                        className="text-xs"
                                        readonly
                                    >
                                        {
                                            defaultPersonas[
                                                Math.floor(
                                                    Math.random() *
                                                        defaultPersonas.length,
                                                )
                                            ].systemPrompt
                                        }
                                    </textarea>
                                </div>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar">
                                <div className="title-bar-text">How to Use</div>
                            </div>
                            <div className="window-body p-4 space-y-2">
                                <p className="text-xs text-gray-800">
                                    • Create new personas with custom system
                                    prompts
                                    <br />• Edit existing personas to change
                                    their behavior
                                    <br />• Use personas in party chats to add
                                    AI participants
                                </p>
                            </div>
                        </div>

                        <div className="flex items-center justify-center p-4">
                            <button
                                type="button"
                                className="min-w-[140px]"
                                hx-get="/personas"
                                hx-target="#windows"
                                hx-swap="beforeend"
                                hx-trigger="click[!window.document.getElementById('personas')]"
                            >
                                Open Personas App →
                            </button>
                        </div>
                    </div>

                    {/* Step 3: Party Chats */}
                    <div
                        x-show="currentStep === 3"
                        className="space-y-4"
                        x-cloak
                    >
                        <div className="flex items-start gap-4 mb-4">
                            <div
                                className="text-6xl leading-none select-none"
                                style="filter: drop-shadow(2px 2px 0px rgba(0,0,0,0.2))"
                            >
                                💬
                            </div>
                            <div className="flex-1">
                                <h1
                                    className="text-2xl font-bold text-[#003399] mb-2"
                                    style="text-shadow: 1px 1px 0px rgba(255,255,255,0.5)"
                                >
                                    Start Party Chats
                                </h1>
                                <p className="text-sm leading-relaxed text-gray-800">
                                    Create or join chat rooms where you can have
                                    conversations with other users and AI
                                    personas. Messages are persistent and
                                    multiple people can participate in the same
                                    room.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar">
                                <div className="title-bar-text">
                                    How It Works
                                </div>
                            </div>
                            <div className="window-body p-4 space-y-3">
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">
                                        →
                                    </span>
                                    <p className="text-sm text-gray-800">
                                        <strong>Create a new party:</strong>{" "}
                                        Start a chat room with a custom name or
                                        use a random one.
                                    </p>
                                </div>
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">
                                        →
                                    </span>
                                    <p className="text-sm text-gray-800">
                                        <strong>Join existing parties:</strong>{" "}
                                        Browse your previous chat rooms or join
                                        one by ID.
                                    </p>
                                </div>
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">
                                        →
                                    </span>
                                    <p className="text-sm text-gray-800">
                                        <strong>
                                            Chat with users and personas:
                                        </strong>{" "}
                                        Have conversations with other logged-in
                                        users and add AI personas to participate
                                        in the discussion.
                                    </p>
                                </div>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar">
                                <div className="title-bar-text">Features</div>
                            </div>
                            <div className="window-body p-4 space-y-2">
                                <p className="text-xs text-gray-800">
                                    • Messages are saved and persist across
                                    sessions
                                    <br />• Multiple users can join the same
                                    chat room
                                    <br />• Add personas to conversations to
                                    have AI participants
                                    <br />• Real-time updates as messages are
                                    sent
                                </p>
                            </div>
                        </div>

                        <div className="flex items-center justify-center p-4">
                            <button
                                type="button"
                                className="min-w-[140px]"
                                hx-get="/party"
                                hx-target="#windows"
                                hx-swap="beforeend"
                                hx-trigger="click[!window.document.getElementById('open-party')]"
                            >
                                Open Party Chat →
                            </button>
                        </div>
                    </div>

                    {/* Step 4: Getting Started */}
                    <div
                        x-show="currentStep === 4"
                        className="space-y-4"
                        x-cloak
                    >
                        <div className="flex items-start gap-4 mb-4">
                            <div
                                className="text-6xl leading-none select-none"
                                style="filter: drop-shadow(2px 2px 0px rgba(0,0,0,0.2))"
                            >
                                ✅
                            </div>
                            <div className="flex-1">
                                <h1
                                    className="text-2xl font-bold text-[#003399] mb-2"
                                    style="text-shadow: 1px 1px 0px rgba(255,255,255,0.5)"
                                >
                                    You're All Set
                                </h1>
                                <p className="text-sm leading-relaxed text-gray-800">
                                    You now know the basics. Log in, create
                                    personas, and start chatting in party rooms.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar">
                                <div className="title-bar-text">
                                    Getting Started
                                </div>
                            </div>
                            <div className="window-body p-4 space-y-3">
                                <p className="text-sm text-gray-800">
                                    Click the buttons in the previous steps to
                                    open the Personas and Party Chat apps, or
                                    use the desktop icons to access them
                                    anytime.
                                </p>
                                <p className="text-xs text-gray-600">
                                    Messages and personas are saved, so you can
                                    return to your conversations later.
                                </p>
                            </div>
                        </div>

                        <div className="text-center text-xs text-gray-600 pt-4">
                            <a
                                href="https://timwillebrands.nl"
                                target="_blank"
                                rel="noopener noreferrer"
                                className="text-gray-600 hover:text-gray-800"
                            >
                                timwillebrands.nl
                            </a>
                        </div>
                    </div>
                </div>

                {/* Navigation Bar */}
                <div className="flex-shrink-0 border-t-2 border-[#D4D0C8] bg-[#ECE9D8] p-4 shadow-[inset_1px_1px_0px_#ffffff,inset_-1px_-1px_0px_#808080]">
                    <div className="flex justify-between items-center">
                        {/* Progress Indicator */}
                        <div className="text-xs text-gray-800">
                            <div className="flex items-center gap-2 mb-2">
                                <span className="font-bold">
                                    Step <span x-text="currentStep"></span> of 4
                                </span>
                            </div>
                            <div className="flex gap-1">
                                <template x-for="i in 4">
                                    <div
                                        className="w-16 h-3 border border-gray-600 bg-white shadow-[inset_-1px_-1px_#ffffff,inset_1px_1px_#808080]"
                                        x-bind:style="i <= currentStep ? 'background: linear-gradient(180deg, #0054E3 0%, #1084D0 100%);' : ''"
                                    ></div>
                                </template>
                            </div>
                        </div>

                        {/* Navigation Buttons */}
                        <div className="flex gap-2">
                            <button
                                type="button"
                                x-show="currentStep > 1"
                                x-on:click="currentStep--"
                                className="px-4 py-1 min-w-[80px]"
                                x-cloak
                            >
                                &lt; Back
                            </button>
                            <button
                                type="button"
                                x-show="currentStep < 4"
                                x-on:click="currentStep++"
                                className="px-4 py-1 min-w-[80px] font-bold"
                                x-cloak
                            >
                                Next &gt;
                            </button>

                            <button
                                type="button"
                                className="px-4 py-1 min-w-[80px] font-bold"
                                x-show="currentStep === 4"
                                x-on:click="$event.target.closest('.window').querySelector('[aria-label=Close]').click()"
                            >
                                Finish
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        </WindowContainer>
    );
}
