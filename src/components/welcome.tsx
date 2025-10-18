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
                                    A playground for LLM experiments. Not too vibecode'y but an 
                                    exploration of multiplayer-first architectures using cheap models.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar bg-gradient-to-r from-[#0054E3] to-[#1084D0]">
                                <div className="title-bar-text text-white font-bold">About This Project</div>
                            </div>
                            <div className="window-body p-4 space-y-3">
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">→</span>
                                    <p className="text-sm text-gray-800">
                                        Built by <strong>Tim Willebrands</strong>, an engineer who likes exploring ideas and building things.
                                    </p>
                                </div>
                                <div className="flex items-start gap-2">
                                    <span className="text-blue-600 font-bold text-sm">→</span>
                                    <p className="text-sm text-gray-800">
                                        Tech choices driven by vibes, engineering constraints and desire to understand hypetech: <strong>Cloudflare Workers</strong> for zero-latency edge deployment, 
                                        <strong>Durable Objects</strong> for coordinating state, <strong>htmx</strong> because https://grugbrain.dev/ is funny and true.
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
                                        Because nostalgia, and I can just follow set patterns without having to think too much. 
                                        Also <code className="bg-white px-1 border border-gray-400">xp.css</code> is surprisingly well-architected. 
                                        Grug approve of constraint-based design.
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
                                    Store prompts like these. Give them names. The implementation uses KV storage—simple, boring, works. 
                                    Grug like simple CRUD. Complexity demon stay away.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar bg-gradient-to-r from-[#D44500] to-[#FF6B35]">
                                <div className="title-bar-text text-white font-bold">Implementation Detail</div>
                            </div>
                            <div className="window-body p-4">
                                <p className="text-xs font-mono text-gray-800 leading-relaxed mb-2">
                                    // Full-stack: route handlers, KV ops,<br/>
                                    // split-pane UI with htmx for partial swaps.<br/>
                                    // Server render HTML, browser swap in DOM.<br/>
                                    // No SPA complexity. Hypermedia go brrr.
                                </p>
                                <p className="text-xs text-gray-700">
                                    Built the whole thing. Designed it, wrote it, made it work. That's the fun part.
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
                                <div className="title-bar-text text-white font-bold">Why This Stack? (The Irony)</div>
                            </div>
                            <div className="window-body p-4 space-y-2">
                                <div className="text-xs space-y-2">
                                    <p>
                                        <strong className="text-blue-700">Cloudflare Workers:</strong> Deploy to 300+ edge locations. 
                                        Is this overkill for a toy chat app? Yes. Is it fun to explore? Also yes.
                                    </p>
                                    <p>
                                        <strong className="text-purple-700">Durable Objects:</strong> SQLite at the edge with WebSocket coordination. 
                                        Grug brain say "complexity bad!" but also grug brain say "ooh shiny edge computing primitives!"
                                    </p>
                                    <p>
                                        <strong className="text-green-700">htmx + SSE:</strong> Server renders HTML, browser swaps it. 
                                        Actually simple despite the backend complexity. <a href="https://htmx.org/essays/" target="_blank" rel="noopener" className="text-blue-600 underline">Read the essays.</a>
                                    </p>
                                </div>
                            </div>
                        </div>

                        <div className="flex flex-col items-center justify-center gap-3 p-4">
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
                            <a 
                                href="https://htmx.org" 
                                target="_blank" 
                                rel="noopener noreferrer"
                                className="opacity-80 hover:opacity-100 transition-opacity"
                            >
                                <img 
                                    src="https://htmx.org/img/createdwith.jpeg" 
                                    alt="Created with htmx"
                                    className="h-8 border border-gray-400 shadow-sm"
                                />
                            </a>
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
                                    Who did this?
                                </h1>
                                <p className="text-sm leading-relaxed text-gray-800">
                                    Tim Willebrands. Lead Engineer at Florinco. Hired as a software engineer 
                                    based on portfolio, before formal training. Pragmatic craftsman approach.
                                </p>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar">
                                <div className="title-bar-text">Engineering Philosophy (grug-inspired)</div>
                            </div>
                            <div className="window-body p-4">
                                <ul className="space-y-2 text-xs leading-relaxed">
                                    <li className="flex items-start gap-2">
                                        <span className="text-blue-600 font-bold">▸</span>
                                        <span><strong>Language-agnostic.</strong> Tools, not identities. Use what works.</span>
                                    </li>
                                    <li className="flex items-start gap-2">
                                        <span className="text-blue-600 font-bold">▸</span>
                                        <span><strong>Maintainable &gt; "clean".</strong> Code other grugs can understand and extend. <a href="https://grugbrain.dev/" target="_blank" rel="noopener" className="text-blue-600 underline">Complexity very bad.</a></span>
                                    </li>
                                    <li className="flex items-start gap-2">
                                        <span className="text-blue-600 font-bold">▸</span>
                                        <span><strong>Automate boring parts.</strong> CI/CD, dev environments—let robots do robot work.</span>
                                    </li>
                                    <li className="flex items-start gap-2">
                                        <span className="text-blue-600 font-bold">▸</span>
                                        <span><strong>Understand your stack.</strong> Use Arch btw. Get hands dirty. Break things to learn how they work.</span>
                                    </li>
                                </ul>
                            </div>
                        </div>

                        <div className="window bg-white shadow-lg">
                            <div className="title-bar bg-gradient-to-r from-[#6B238E] to-[#9B59B6]">
                                <div className="title-bar-text text-white font-bold">Selected Work & Explorations</div>
                            </div>
                            <div className="window-body p-4 space-y-3 text-xs">
                                <div>
                                    <div className="font-bold text-purple-800">Auxin (Real-Time Data Platform)</div>
                                    <p className="text-gray-700">
                                        Federated GraphQL gateway, modular frontend for third-party embeds, 
                                        spatially-aware enrichment engine. Designed it, built it, maintained it.
                                    </p>
                                </div>
                                <div>
                                    <div className="font-bold text-purple-800">GitHub Playground</div>
                                    <p className="text-gray-700">
                                        PubbieSubbie (queueing), use-communal-state (CRDT), B-Trees in Rust, 
                                        raycasting in C3→WASM. Building stuff to understand how it works, not just use it.
                                    </p>
                                </div>
                                <div>
                                    <div className="font-bold text-purple-800">This Project</div>
                                    <p className="text-gray-700">
                                        Overengineered on purpose to explore edge computing, Durable Objects, and real-time patterns. 
                                        Also an excuse to build something with XP aesthetics because why not.
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

