import { WindowContainer } from "./window";

export type Party = {
    id: string;
    name: string;
    lastActivity?: string;
    messageCount?: number;
};

export function OpenParty({ previousParties }: { previousParties: Party[] }) {
    return (
        <WindowContainer id="open-party" title="🎉 Party Chat" url="/party">
            <div className="window-body flex flex-col h-full">
                {/* Header */}
                <div className="p-4 border-b border-gray-300 bg-gradient-to-r from-purple-50 to-pink-50">
                    <h2 className="font-bold text-lg text-purple-900">
                        🎉 Welcome to Party Chat!
                    </h2>
                    <p className="text-sm text-gray-600">
                        Start a new party or join an existing one.
                    </p>
                </div>

                {/* Previous Parties List */}
                <div className="flex h-full">
                    <div className="flex-1 overflow-y-auto p-4 h-full bg-gradient-to-b from-gray-50 to-white border-r border-gray-300">
                        <div className="mb-4">
                            <h3 className="font-bold mb-4 text-gray-700 text-sm uppercase tracking-wide">
                                📝 Previous Parties
                            </h3>

                            {previousParties.length === 0 ? (
                                <div className="text-center text-gray-600 py-10 border-2 border-dashed border-gray-300 rounded-lg bg-white">
                                    <div className="text-3xl mb-3">🎈</div>
                                    <div className="text-sm font-medium">No previous parties found</div>
                                    <div className="text-xs text-gray-500 mt-2">
                                        Start your first party below!
                                    </div>
                                </div>
                            ) : (
                                <div className="space-y-2.5">
                                    {previousParties.map((party) => (
                                        <div
                                            key={party.id}
                                            className="group border-2 border-gray-300 rounded-lg p-3 hover:bg-purple-50 hover:border-purple-400 hover:shadow-md cursor-pointer transition-all duration-200 bg-white shadow-sm"
                                            hx-get={`/party/${party.id}`}
                                            hx-target="#open-party"
                                            hx-swap="outerHTML"
                                        >
                                            <div className="flex items-center justify-between">
                                                <div className="flex-1">
                                                    <div className="font-semibold text-gray-800 group-hover:text-purple-800">
                                                        🎉{" "}
                                                        {party.name || party.id}
                                                    </div>
                                                    <div className="text-xs text-gray-500 mt-1">
                                                        {party.lastActivity && (
                                                            <span>
                                                                Last: {party.lastActivity}
                                                            </span>
                                                        )}
                                                        {party.messageCount !==
                                                            undefined && (
                                                            <span className="ml-2">
                                                                💬 {party.messageCount}
                                                            </span>
                                                        )}
                                                    </div>
                                                </div>
                                                <div className="opacity-0 group-hover:opacity-100 transition-opacity">
                                                    <svg
                                                        className="w-5 h-5 text-purple-500"
                                                        fill="none"
                                                        stroke="currentColor"
                                                        viewBox="0 0 24 24"
                                                    >
                                                        <title>Join party</title>
                                                        <path
                                                            strokeLinecap="round"
                                                            strokeLinejoin="round"
                                                            strokeWidth="2"
                                                            d="M9 5l7 7-7 7"
                                                        />
                                                    </svg>
                                                </div>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
                    </div>

                    {/* New Party Section */}
                    <div className="p-4 bg-gradient-to-br from-white to-purple-50/30 h-full w-1/2">
                        <h3 className="font-bold mb-4 text-gray-700 text-sm uppercase tracking-wide">
                            🚀 Start New Party
                        </h3>

                        <form
                            hx-post="/party/create"
                            hx-target="#open-party"
                            hx-swap="outerHTML"
                            className="space-y-4"
                            hx-on:before-request="
                                const btn = event.target.querySelector('[type=submit]');
                                btn.disabled = true;
                                btn.textContent = '⏳ Creating...';
                            "
                            {...{
                                "x-on:htmx:after-request.camel": `
                                windows[windowId] = undefined;
                                $event.target.closest('.window').remove(); `,
                            }}
                        >
                            <div className="field-row-stacked">
                                <label
                                    htmlFor="party-name"
                                    className="font-bold text-sm"
                                >
                                    🏷️ Party Name (optional):
                                </label>
                                <input
                                    type="text"
                                    name="partyName"
                                    id="party-name"
                                    placeholder="Enter a fun name for your party..."
                                    className="w-full font-sans"
                                    maxLength={50}
                                />
                                <div className="text-xs text-gray-600 mt-1">
                                    💡 Leave blank to generate a random party ID
                                </div>
                            </div>

                            <div className="flex justify-end pt-2">
                                <button
                                    type="submit"
                                    className="font-bold px-4 py-2 min-w-[120px]"
                                >
                                    🎉 Start Party
                                </button>
                            </div>
                        </form>

                        {/* Quick Actions */}
                        <div className="mt-6 pt-4 border-t border-gray-300">
                            <div className="text-sm font-bold text-gray-700 mb-3">
                                ⚡ Quick Actions:
                            </div>
                            <div className="flex gap-2 flex-wrap">
                                <button
                                    type="button"
                                    className="text-sm px-3 py-1.5"
                                    hx-post="/party/create"
                                    hx-vals='{"partyName": "Random Chat"}'
                                    hx-target="body"
                                    hx-push-url="true"
                                >
                                    🎲 Random
                                </button>
                                <button
                                    type="button"
                                    className="text-sm px-3 py-1.5"
                                    hx-get="/party/join"
                                    hx-target="#open-party .window-body"
                                    hx-swap="innerHTML"
                                >
                                    🔗 Join by ID
                                </button>
                            </div>
                        </div>
                    </div>
                </div>

                {/* Status Bar */}
                <div className="status-bar">
                    <p className="status-bar-field">🟢 Ready</p>
                    <p className="status-bar-field">{previousParties.length} previous part{previousParties.length !== 1 ? 'ies' : 'y'}</p>
                    <p className="status-bar-field">💬 Chat service online</p>
                </div>
            </div>
        </WindowContainer>
    );
}
