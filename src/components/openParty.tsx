import { WindowContainer } from "./window";

export function OpenParty({
    previousParties,
}: {
    previousParties: {
        id: string;
        name: string;
        lastActivity?: string;
        messageCount?: number;
    }[];
}) {
    return (
        <WindowContainer id="open-party" title="🎉 Open Party" url="/party">
            <div className="window-body flex flex-col h-full">
                {/* Header */}
                <div className="p-4 border-b border-gray-300">
                    <h2 className="font-bold text-lg mb-2">
                        🎉 Welcome to Party Chat!
                    </h2>
                    <p className="text-sm text-gray-600">
                        Start a new party or join an existing one.
                    </p>
                </div>

                {/* Previous Parties List */}
                <div className="flex h-full">
                    <div className="flex-1 overflow-y-auto p-4 h-full">
                        <div className="mb-4">
                            <h3 className="font-bold mb-3 text-blue-800">
                                📝 Previous Parties
                            </h3>

                            {previousParties.length === 0 ? (
                                <div className="text-center text-gray-500 py-8 border border-dashed border-gray-300 rounded">
                                    <div className="text-2xl mb-2">🎈</div>
                                    <div>No previous parties found</div>
                                    <div className="text-xs mt-1">
                                        Start your first party below!
                                    </div>
                                </div>
                            ) : (
                                <div className="space-y-2">
                                    {previousParties.map((party) => (
                                        <div
                                            key={party.id}
                                            className="group border border-gray-300 p-3 hover:bg-gray-50 cursor-pointer transition-colors shadow-sm rounded"
                                            hx-get={`/party/${party.id}`}
                                            hx-target="body"
                                            hx-push-url="true"
                                        >
                                            <div className="flex items-center justify-between">
                                                <div className="flex-1">
                                                    <div className="font-semibold text-blue-800 group-hover:text-blue-900">
                                                        🎉{" "}
                                                        {party.name || party.id}
                                                    </div>
                                                    <div className="text-xs text-gray-500 mt-1">
                                                        {party.lastActivity && (
                                                            <span>
                                                                Last active:{" "}
                                                                {
                                                                    party.lastActivity
                                                                }
                                                            </span>
                                                        )}
                                                        {party.messageCount !==
                                                            undefined && (
                                                            <span className="ml-2">
                                                                💬{" "}
                                                                {
                                                                    party.messageCount
                                                                }{" "}
                                                                messages
                                                            </span>
                                                        )}
                                                    </div>
                                                </div>
                                                <div className="text-gray-400 group-hover:text-blue-600">
                                                    →
                                                </div>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
                    </div>

                    {/* New Party Section */}
                    <div className="border-t border-gray-300 p-4 bg-gray-50 h-full">
                        <h3 className="font-bold mb-3 text-blue-800">
                            🚀 Start New Party
                        </h3>

                        <form
                            hx-post="/party/create"
                            hx-target="#open-party"
                            hx-swap="outerHTML"
                            className="space-y-3"
                            hx-on:before-request="
                                const btn = event.target.querySelector('[type=submit]');
                                btn.disabled = true;
                                btn.textContent = '⏳ Creating...';
                            "
                        >
                            <div className="field-row-stacked">
                                <label
                                    htmlFor="party-name"
                                    className="font-bold text-sm"
                                >
                                    🏷 Party Name (optional):
                                </label>
                                <input
                                    type="text"
                                    name="partyName"
                                    id="party-name"
                                    placeholder="Enter a fun name for your party..."
                                    className="w-full font-sans text-[11px]"
                                    maxLength={50}
                                />
                                <div className="text-xs text-gray-500 mt-1">
                                    Leave blank to generate a random party ID
                                </div>
                            </div>

                            <div className="field-row justify-between items-center">
                                <div className="text-xs text-gray-600">
                                    💡 Tip: Share the party URL with friends to
                                    chat together!
                                </div>
                                <button
                                    type="submit"
                                    className="font-bold px-4 py-2 min-w-[120px]"
                                >
                                    🎉 Start Party
                                </button>
                            </div>
                        </form>

                        {/* Quick Actions */}
                        <div className="mt-4 pt-3 border-t border-gray-200">
                            <div className="text-xs text-gray-500 mb-2">
                                Quick Actions:
                            </div>
                            <div className="flex gap-2 flex-wrap">
                                <button
                                    type="button"
                                    className="text-xs px-2 py-1 border border-gray-300 hover:bg-white"
                                    hx-post="/party/create"
                                    hx-vals='{"partyName": "Random Chat"}'
                                    hx-target="body"
                                    hx-push-url="true"
                                >
                                    🎲 Random Party
                                </button>
                                <button
                                    type="button"
                                    className="text-xs px-2 py-1 border border-gray-300 hover:bg-white"
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
            </div>
        </WindowContainer>
    );
}
