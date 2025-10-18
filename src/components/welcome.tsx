import { WindowContainer } from "./window";

export function Welcome() {
    return (
        <WindowContainer id="welcome" title="Welcome to Proompt.party" url="/welcome">
            <div 
                className="window-body flex flex-col h-full p-0 bg-[#ECE9D8]"
                x-data="{ currentStep: 1 }"
            >
                {/* Content Area */}
                <div className="flex-1 overflow-y-auto p-6">
                    {/* Step 1: Introduction */}
                    <div x-show="currentStep === 1" className="space-y-4">
                        <div className="flex items-start gap-4 mb-6">
                            <div className="text-7xl leading-none select-none" style="filter: drop-shadow(2px 2px 0px rgba(0,0,0,0.2))">
                                🎪
                            </div>
                            <div className="flex-1">
                                <h1 className="text-2xl font-bold text-[#003399] mb-2" style="text-shadow: 1px 1px 0px rgba(255,255,255,0.5)">
                                    Welcome to Proompt.party
                                </h1>
                                <p className="text-sm leading-relaxed text-gray-800">
                                    A playground for LLM experiments. No corporate AI nonsense—just 
                                    hands-on exploration of multiplayer-first architectures using free models.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white border-2 border-[#0054E3] shadow-lg">
                            <div className="title-bar bg-gradient-to-r from-[#0054E3] to-[#1084D0]">
                                <div className="title-bar-text text-white font-bold">About This Project</div>
                            </div>
                            <div className="window-body p-4 space-y-3">
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">→</span>
                                    <p className="text-sm text-gray-800">
                                        Built by <strong>Tim Willebrands</strong>, an engineer who actually writes code (not a manager who "directs implementation")
                                    </p>
                                </div>
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">→</span>
                                    <p className="text-sm text-gray-800">
                                        Tech choices driven by engineering constraints, not hype: <strong>Cloudflare Workers</strong> for zero-latency edge deployment, 
                                        <strong>Durable Objects</strong> for stateful WebSocket rooms, <strong>htmx</strong> because sometimes hypermedia is the right tool
                                    </p>
                                </div>
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">→</span>
                                    <p className="text-sm text-gray-800">
                                        Serves as a portfolio (yes, this whole XP aesthetic is deliberate), but more importantly as an exploration space 
                                        for real-time collaboration patterns with LLMs
                                    </p>
                                </div>
                            </div>
                        </div>

                        <div className="bg-gradient-to-r from-[#FFF8DC] to-[#FFFACD] border-2 border-[#F0E68C] p-4 shadow-[inset_-1px_-1px_#808080,inset_1px_1px_#ffffff,inset_-2px_-2px_#C0C0C0,inset_2px_2px_#DFDFDF]">
                            <div className="flex items-start gap-3">
                                <div className="text-3xl">💡</div>
                                <div>
                                    <p className="font-bold text-sm text-gray-900 mb-1">Why Windows XP?</p>
                                    <p className="text-xs text-gray-700 leading-relaxed">
                                        Because nostalgia is fun, and building a coherent visual system from constraints 
                                        (XP UI patterns) is more interesting than chasing the latest design trends. 
                                        Plus, <code className="bg-white px-1 border border-gray-400">xp.css</code> is a great example of maintainable, 
                                        focused CSS architecture.
                                    </p>
                                </div>
                            </div>
                        </div>
                    </div>

                    {/* Step 2: Personas Feature */}
                    <div x-show="currentStep === 2" className="space-y-4" x-cloak>
                        <div className="flex items-start gap-4 mb-4">
                            <div className="text-6xl leading-none select-none" style="filter: drop-shadow(2px 2px 0px rgba(0,0,0,0.2))">
                                🎭
                            </div>
                            <div className="flex-1">
                                <h1 className="text-2xl font-bold text-[#003399] mb-2" style="text-shadow: 1px 1px 0px rgba(255,255,255,0.5)">
                                    Personas
                                </h1>
                                <p className="text-sm leading-relaxed text-gray-800">
                                    Manage reusable system prompts. Nothing fancy—just a CRUD interface 
                                    for storing prompt templates you want to reuse across conversations.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar">
                                <div className="title-bar-text">How It Works</div>
                            </div>
                            <div className="window-body p-4 space-y-3">
                                <div className="field-row-stacked">
                                    <label className="font-bold text-xs">SYSTEM PROMPT</label>
                                    <textarea 
                                        rows={3} 
                                        className="text-xs" 
                                        readonly
                                        value="You are a helpful assistant who explains complex topics using simple analogies."
                                    ></textarea>
                                </div>
                                <p className="text-xs text-gray-700 leading-relaxed">
                                    Store prompts like these. Give them names. Use them in chat rooms. 
                                    The implementation uses KV storage for persistence—simple, boring, works.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar bg-gradient-to-r from-[#D44500] to-[#FF6B35]">
                                <div className="title-bar-text text-white font-bold">Implementation Detail</div>
                            </div>
                            <div className="window-body p-4">
                                <p className="text-xs font-mono text-gray-800 leading-relaxed mb-2">
                                    // Full-stack ownership: route handlers, KV ops,<br/>
                                    // split-pane UI with htmx for partial updates.<br/>
                                    // No framework overhead—just Hono + htmx.
                                </p>
                                <p className="text-xs text-gray-700">
                                    This is what "I built it" means: designed the data model, wrote the routes, 
                                    built the UI, made it work. End-to-end.
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
                    <div x-show="currentStep === 3" className="space-y-4" x-cloak>
                        <div className="flex items-start gap-4 mb-4">
                            <div className="text-6xl leading-none select-none" style="filter: drop-shadow(2px 2px 0px rgba(0,0,0,0.2))">
                                💬
                            </div>
                            <div className="flex-1">
                                <h1 className="text-2xl font-bold text-[#003399] mb-2" style="text-shadow: 1px 1px 0px rgba(255,255,255,0.5)">
                                    Party Chats
                                </h1>
                                <p className="text-sm leading-relaxed text-gray-800">
                                    Stateful, persistent chat rooms. The interesting part is the architecture: 
                                    Durable Objects for room state, WebSocket pub/sub, SSE for streaming LLM responses to browser.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar">
                                <div className="title-bar-text">Technical Overview</div>
                            </div>
                            <div className="window-body p-4 space-y-3">
                                <div className="space-y-2 text-xs font-mono bg-gray-50 p-3 border border-gray-300">
                                    <div><span className="text-blue-600">Browser</span> → htmx SSE connection</div>
                                    <div className="pl-4">↓</div>
                                    <div><span className="text-green-600">Hono</span> → WebSocket to Durable Object</div>
                                    <div className="pl-4">↓</div>
                                    <div><span className="text-purple-600">Durable Object</span> → SQLite + WebSocket pub/sub</div>
                                    <div className="pl-4">↓</div>
                                    <div><span className="text-orange-600">OpenRouter</span> → LLM streaming response</div>
                                </div>
                                <p className="text-xs text-gray-700">
                                    Each room is a Durable Object instance with its own SQLite database. 
                                    Messages persist. Multiple clients can connect. Responses stream token-by-token.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar bg-gradient-to-r from-[#0078D7] to-[#00BCF2]">
                                <div className="title-bar-text text-white font-bold">Why This Stack?</div>
                            </div>
                            <div className="window-body p-4 space-y-2">
                                <div className="text-xs space-y-2">
                                    <p>
                                        <strong className="text-blue-700">Cloudflare Workers:</strong> Deploy to 300+ edge locations globally. 
                                        Zero cold starts. No servers to manage.
                                    </p>
                                    <p>
                                        <strong className="text-purple-700">Durable Objects:</strong> Stateful coordination primitive. 
                                        SQLite at the edge. WebSocket hibernation for efficient resource use.
                                    </p>
                                    <p>
                                        <strong className="text-green-700">htmx + SSE:</strong> Hypermedia over WebSocket complexity. 
                                        Server renders HTML, client swaps it. Simple, works.
                                    </p>
                                </div>
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

                    {/* Step 4: About Tim */}
                    <div x-show="currentStep === 4" className="space-y-4" x-cloak>
                        <div className="flex items-start gap-4 mb-4">
                            <div className="text-6xl leading-none select-none" style="filter: drop-shadow(2px 2px 0px rgba(0,0,0,0.2))">
                                🛠️
                            </div>
                            <div className="flex-1">
                                <h1 className="text-2xl font-bold text-[#003399] mb-2" style="text-shadow: 1px 1px 0px rgba(255,255,255,0.5)">
                                    About the Builder
                                </h1>
                                <p className="text-sm leading-relaxed text-gray-800">
                                    Tim Willebrands. Lead Engineer at Florinco. Hired as a software engineer 
                                    based on hobbyist portfolio strength, before formal training. Pragmatic craftsman approach.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar">
                                <div className="title-bar-text">Engineering Philosophy</div>
                            </div>
                            <div className="window-body p-4">
                                <ul className="space-y-2 text-xs leading-relaxed">
                                    <li className="flex items-start gap-2">
                                        <span className="text-blue-600 font-bold">▸</span>
                                        <span><strong>Language-agnostic.</strong> Tools, not identities. Use what solves the problem.</span>
                                    </li>
                                    <li className="flex items-start gap-2">
                                        <span className="text-blue-600 font-bold">▸</span>
                                        <span><strong>Maintainable > "clean".</strong> Pragmatic code that others can understand and extend.</span>
                                    </li>
                                    <li className="flex items-start gap-2">
                                        <span className="text-blue-600 font-bold">▸</span>
                                        <span><strong>Automate everything.</strong> CI/CD, dev environments—highest leverage activities.</span>
                                    </li>
                                    <li className="flex items-start gap-2">
                                        <span className="text-blue-600 font-bold">▸</span>
                                        <span><strong>Understand the stack.</strong> Use Arch Linux btw. Know your tools from the ground up.</span>
                                    </li>
                                </ul>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar bg-gradient-to-r from-[#6B238E] to-[#9B59B6]">
                                <div className="title-bar-text text-white font-bold">Selected Work</div>
                            </div>
                            <div className="window-body p-4 space-y-3 text-xs">
                                <div>
                                    <div className="font-bold text-purple-800">Auxin (Real-Time Data Platform)</div>
                                    <p className="text-gray-700">
                                        Architected federated GraphQL gateway, modular frontend for third-party embeds, 
                                        spatially-aware enrichment engine. Not "led the team"—designed it, built it, owned it.
                                    </p>
                                </div>
                                <div>
                                    <div className="font-bold text-purple-800">GitHub Explorations</div>
                                    <p className="text-gray-700">
                                        Queueing systems (PubbieSubbie), CRDT state management (use-communal-state), 
                                        B-Trees in Rust. Non-trivial explorations demonstrating continuous learning.
                                    </p>
                                </div>
                            </div>
                        </div>

                        <div className="flex items-center justify-between gap-3 p-4 bg-[#F3F1ED] border-2 border-[#D4D0C8] shadow-[inset_-1px_-1px_#808080,inset_1px_1px_#ffffff]">
                            <div className="flex gap-2">
                                <a 
                                    href="https://github.com/TimWillebrands/" 
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="flex items-center gap-2 px-3 py-2 text-xs"
                                    style="min-width: 100px;"
                                >
                                    <span className="text-lg">⚡</span>
                                    <span>GitHub</span>
                                </a>
                                <a 
                                    href="https://www.linkedin.com/in/tim-willebrands" 
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="flex items-center gap-2 px-3 py-2 text-xs"
                                    style="min-width: 100px;"
                                >
                                    <span className="text-lg">💼</span>
                                    <span>LinkedIn</span>
                                </a>
                            </div>
                            <button
                                type="button"
                                className="px-4 py-2 min-w-[120px] font-bold"
                                x-on:click="$event.target.closest('.window').querySelector('[aria-label=Close]').click()"
                            >
                                Close Tour
                            </button>
                        </div>
                    </div>
                </div>

                {/* Navigation Bar */}
                <div className="border-t-2 border-[#D4D0C8] bg-[#ECE9D8] p-4 shadow-[inset_1px_1px_0px_#ffffff,inset_-1px_-1px_0px_#808080]">
                    <div className="flex justify-between items-center">
                        {/* Progress Indicator */}
                        <div className="text-xs text-gray-800">
                            <div className="flex items-center gap-2 mb-2">
                                <span className="font-bold">Step <span x-text="currentStep"></span> of 4</span>
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
                        </div>
                    </div>
                </div>
            </div>
        </WindowContainer>
    );
}

