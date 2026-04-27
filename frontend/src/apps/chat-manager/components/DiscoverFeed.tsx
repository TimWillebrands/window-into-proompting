import RoomCard from './RoomCard';
import {
    ALL_STUB_ROOMS,
    FEATURED_ROOMS,
    POPULAR_ROOMS,
    paletteFor,
    RECENT_ROOMS,
} from './stub-data';

interface RealRoom {
    id: string;
    name: string;
    scenario?: string | null;
    createdAt?: string | number | null;
}

interface DiscoverFeedProps {
    realRooms: RealRoom[];
    onCreateRoom: (name: string, scenario: string) => void;
    creating: boolean;
    searchQuery: string;
    onSelectRealRoom: (id: string) => void;
    inputName: string;
    inputScenario: string;
    onInputName: (v: string) => void;
    onInputScenario: (v: string) => void;
}

export default function DiscoverFeed({
    realRooms,
    onCreateRoom,
    creating,
    searchQuery,
    onSelectRealRoom,
    inputName,
    inputScenario,
    onInputName,
    onInputScenario,
}: DiscoverFeedProps) {
    const q = searchQuery.trim().toLowerCase();
    if (q) {
        const matchedStubs = ALL_STUB_ROOMS.filter((r) =>
            r.name.toLowerCase().includes(q),
        );
        const matchedReal = realRooms.filter((r) =>
            r.name.toLowerCase().includes(q),
        );
        return (
            <div className="flex-1 overflow-y-auto px-6 py-5 dark:text-slate-100">
                <h2 className="mb-3 text-lg font-bold text-slate-900 dark:text-slate-100">
                    Search results for &ldquo;{searchQuery}&rdquo;
                </h2>
                {matchedReal.length === 0 && matchedStubs.length === 0 ? (
                    <p className="text-sm text-slate-500 dark:text-slate-400">
                        No rooms match. Try another query.
                    </p>
                ) : (
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                        {matchedReal.map((r) => {
                            const p = paletteFor(r.id);
                            return (
                                <RoomCard
                                    key={r.id}
                                    name={r.name}
                                    description={
                                        r.scenario || 'A room you have joined.'
                                    }
                                    emoji={p.emoji}
                                    gradient={p.gradient}
                                    online={0}
                                    members={1}
                                    size="md"
                                    onClick={() => onSelectRealRoom(r.id)}
                                />
                            );
                        })}
                        {matchedStubs.map((r) => (
                            <RoomCard
                                key={r.id}
                                name={r.name}
                                description={r.description}
                                emoji={r.emoji}
                                gradient={r.gradient}
                                online={r.online}
                                members={r.members}
                                size="md"
                                disabled
                            />
                        ))}
                    </div>
                )}
            </div>
        );
    }

    return (
        <div className="flex-1 overflow-y-auto px-6 py-5 dark:text-slate-100">
            {/* Hero banner */}
            <section className="relative mb-6 overflow-hidden rounded-3xl shadow-lg">
                <div
                    className="relative flex h-48 items-end p-6 text-white"
                    style={{
                        backgroundImage:
                            'linear-gradient(to bottom, #4a90d9 0%, #76b8e8 50%, #a4d3f4 70%, #5cb85c 70%, #2f9c2f 100%)',
                    }}
                >
                    {/* Cloud decorations */}
                    <span
                        aria-hidden
                        className="absolute left-8 top-6 h-6 w-16 rounded-full bg-white/70 blur-sm"
                    />
                    <span
                        aria-hidden
                        className="absolute left-14 top-3 h-4 w-12 rounded-full bg-white/60 blur-sm"
                    />
                    <span
                        aria-hidden
                        className="absolute right-12 top-8 h-7 w-20 rounded-full bg-white/70 blur-sm"
                    />
                    <span
                        aria-hidden
                        className="absolute right-6 top-12 text-3xl"
                    >
                        ✨
                    </span>
                    <div className="relative">
                        <h1 className="text-3xl font-bold drop-shadow">
                            Find Your Community on Daccord
                        </h1>
                        <p className="mt-1 max-w-md text-sm text-white/90 drop-shadow">
                            Discover, connect, and create with people who share
                            your passions.
                        </p>
                    </div>
                </div>
                <div className="absolute bottom-3 left-1/2 flex -translate-x-1/2 gap-1.5">
                    <span className="h-1.5 w-4 rounded-full bg-white" />
                    <span className="h-1.5 w-1.5 rounded-full bg-white/60" />
                    <span className="h-1.5 w-1.5 rounded-full bg-white/60" />
                </div>
            </section>

            {/* Featured */}
            <SectionHeader title="Featured Community" />
            <div className="mb-6 grid grid-cols-1 gap-4 md:grid-cols-2">
                {FEATURED_ROOMS.map((r) => (
                    <RoomCard
                        key={r.id}
                        name={r.name}
                        description={r.description}
                        emoji={r.emoji}
                        gradient={r.gradient}
                        online={r.online}
                        members={r.members}
                        size="lg"
                        disabled
                    />
                ))}
            </div>

            {/* Popular */}
            <SectionHeader title="Popular Right Now" />
            <div className="mb-6 grid grid-cols-1 gap-4 md:grid-cols-2">
                {POPULAR_ROOMS.map((r) => (
                    <RoomCard
                        key={r.id}
                        name={r.name}
                        description={r.description}
                        emoji={r.emoji}
                        gradient={r.gradient}
                        online={r.online}
                        members={r.members}
                        size="md"
                        disabled
                    />
                ))}
            </div>

            {/* Recent stubs */}
            <SectionHeader title="Recent Add" />
            <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-3">
                {RECENT_ROOMS.map((r) => (
                    <RoomCard
                        key={r.id}
                        name={r.name}
                        description={r.description}
                        emoji={r.emoji}
                        gradient={r.gradient}
                        online={r.online}
                        members={r.members}
                        size="md"
                        disabled
                    />
                ))}
            </div>

            {/* Your real rooms (joinable) */}
            {realRooms.length > 0 && (
                <>
                    <SectionHeader title="Rooms You've Joined" />
                    <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-3">
                        {realRooms.map((r) => {
                            const p = paletteFor(r.id);
                            return (
                                <RoomCard
                                    key={r.id}
                                    name={r.name}
                                    description={
                                        r.scenario ||
                                        'A room you have joined. Click to enter.'
                                    }
                                    emoji={p.emoji}
                                    gradient={p.gradient}
                                    online={0}
                                    members={1}
                                    size="md"
                                    onClick={() => onSelectRealRoom(r.id)}
                                />
                            );
                        })}
                    </div>
                </>
            )}

            {/* Create-room block (kept functional — uses real backend) */}
            <SectionHeader title="Create a Room" />
            <form
                className="mb-10 flex flex-col gap-2 rounded-2xl bg-white/70 dark:bg-slate-800/70 p-4 shadow-sm ring-1 ring-slate-200 dark:ring-slate-700 backdrop-blur-sm"
                onSubmit={(e) => {
                    e.preventDefault();
                    if (creating) return;
                    const name = inputName.trim();
                    if (!name) return;
                    onCreateRoom(name, inputScenario.trim());
                }}
            >
                <input
                    type="text"
                    value={inputName}
                    onChange={(e) => onInputName(e.currentTarget.value)}
                    placeholder="Room name…"
                    className="w-full rounded-xl border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-900 px-3 py-2 text-sm text-slate-800 dark:text-slate-100 outline-none focus:border-blue-500"
                />
                <textarea
                    value={inputScenario}
                    onChange={(e) => onInputScenario(e.currentTarget.value)}
                    rows={2}
                    maxLength={500}
                    placeholder="Scenario (optional) — e.g. 'Office of a stealth horticulture startup.'"
                    className="w-full rounded-xl border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-900 px-3 py-2 text-sm text-slate-800 dark:text-slate-100 outline-none focus:border-blue-500"
                />
                <button
                    type="submit"
                    disabled={creating || !inputName.trim()}
                    className="self-start rounded-xl bg-gradient-to-br from-blue-500 to-indigo-600 px-4 py-1.5 text-sm font-semibold text-white shadow hover:from-blue-600 hover:to-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
                >
                    {creating ? 'Creating…' : '+ Create Room'}
                </button>
            </form>
        </div>
    );
}

function SectionHeader({ title }: { title: string }) {
    return (
        <div className="mb-3 flex items-center justify-between">
            <h2 className="text-base font-bold text-slate-900 dark:text-slate-100">
                {title}
            </h2>
            <button
                type="button"
                disabled
                className="text-xs font-semibold text-blue-600 dark:text-blue-400 cursor-not-allowed opacity-70"
            >
                See all
            </button>
        </div>
    );
}
