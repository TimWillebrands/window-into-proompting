import { useId } from 'react';
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
    searchQuery: string;
    onSelectRealRoom: (id: string) => void;
    onOpenCreate: () => void;
}

export default function DiscoverFeed({
    realRooms,
    searchQuery,
    onSelectRealRoom,
    onOpenCreate,
}: DiscoverFeedProps) {
    const grassGradId = useId();
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
                    <div className="grid grid-cols-1 gap-4 @[520px]:grid-cols-2">
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
            {/* Hero banner — XP Bliss-inspired sky/grass with proper cloud SVGs */}
            <section className="relative mb-6 overflow-hidden rounded-[28px] shadow-[0_14px_30px_-8px_rgba(46,92,202,0.45),0_4px_8px_-2px_rgba(0,0,0,0.15)] ring-1 ring-white/50">
                {/* XP-glass top sheen on hero */}
                <span
                    aria-hidden
                    className="pointer-events-none absolute inset-x-0 top-0 z-10 h-1/3 bg-gradient-to-b from-white/30 to-transparent"
                />
                <div className="relative flex h-40 @[520px]:h-52 items-end p-4 @[520px]:p-6 text-white">
                    {/* Sky layer */}
                    <div
                        aria-hidden
                        className="absolute inset-x-0 top-0 h-[68%]"
                        style={{
                            background:
                                'linear-gradient(to bottom, #2c6dc9 0%, #4d92e0 45%, #87bcec 85%, #b8dcf3 100%)',
                        }}
                    />
                    {/* Rolling grass with curved horizon */}
                    <svg
                        role="presentation"
                        className="absolute inset-x-0 top-[60%] h-[42%] w-full"
                        viewBox="0 0 600 90"
                        preserveAspectRatio="none"
                    >
                        <defs>
                            <linearGradient
                                id={grassGradId}
                                x1="0"
                                y1="0"
                                x2="0"
                                y2="1"
                            >
                                <stop offset="0%" stopColor="#7cc05a" />
                                <stop offset="55%" stopColor="#4ea63b" />
                                <stop offset="100%" stopColor="#2f7d24" />
                            </linearGradient>
                        </defs>
                        <path
                            d="M0,28 C 130,4 260,42 380,18 C 480,-2 560,18 600,12 L600,90 L0,90 Z"
                            fill={`url(#${grassGradId})`}
                        />
                    </svg>

                    {/* Cloud SVGs (proper bumpy shapes) */}
                    <svg
                        role="presentation"
                        className="absolute left-6 top-5 h-8 w-28 text-white drop-shadow-[0_2px_4px_rgba(0,0,0,0.15)]"
                        viewBox="0 0 120 36"
                    >
                        <path
                            fill="currentColor"
                            fillOpacity="0.92"
                            d="M14 26 C 4 26 4 14 14 14 C 14 6 26 4 30 12 C 34 4 50 4 52 14 C 64 12 70 22 60 26 Z"
                        />
                    </svg>
                    <svg
                        role="presentation"
                        className="absolute left-32 top-12 h-6 w-20 text-white drop-shadow-[0_2px_4px_rgba(0,0,0,0.15)]"
                        viewBox="0 0 120 36"
                    >
                        <path
                            fill="currentColor"
                            fillOpacity="0.85"
                            d="M14 26 C 4 26 4 14 14 14 C 14 6 26 4 30 12 C 34 4 50 4 52 14 C 64 12 70 22 60 26 Z"
                        />
                    </svg>
                    <svg
                        role="presentation"
                        className="absolute right-10 top-6 h-9 w-32 text-white drop-shadow-[0_2px_4px_rgba(0,0,0,0.15)]"
                        viewBox="0 0 120 36"
                    >
                        <path
                            fill="currentColor"
                            fillOpacity="0.95"
                            d="M14 26 C 4 26 4 14 14 14 C 14 6 26 4 30 12 C 34 4 50 4 52 14 C 64 12 70 22 60 26 Z"
                        />
                    </svg>

                    {/* Sparkle cluster */}
                    <span
                        aria-hidden
                        className="absolute right-8 top-14 text-3xl text-yellow-300 drop-shadow-[0_0_10px_rgba(253,224,71,0.8)]"
                    >
                        ✨
                    </span>
                    <span
                        aria-hidden
                        className="absolute right-20 top-10 text-base text-yellow-200 drop-shadow-[0_0_6px_rgba(253,230,138,0.8)]"
                    >
                        ✦
                    </span>
                    <span
                        aria-hidden
                        className="absolute right-6 top-24 text-sm text-yellow-200/90"
                    >
                        ✦
                    </span>

                    <div className="relative max-w-[70%]">
                        <h1 className="text-lg @[520px]:text-3xl font-bold leading-tight drop-shadow-[0_2px_4px_rgba(0,0,0,0.4)]">
                            Find Your Community on D'Accord
                        </h1>
                        <p className="mt-1 max-w-md text-[11px] @[520px]:text-sm text-white/95 drop-shadow-[0_1px_2px_rgba(0,0,0,0.4)]">
                            Discover, connect, and create with people who share
                            your passions.
                        </p>
                    </div>
                </div>
                <div className="absolute bottom-3 left-1/2 flex -translate-x-1/2 gap-1.5">
                    <span className="h-1.5 w-4 rounded-full bg-white shadow" />
                    <span className="h-1.5 w-1.5 rounded-full bg-white/70" />
                    <span className="h-1.5 w-1.5 rounded-full bg-white/70" />
                </div>
            </section>

            {/* Featured */}
            <SectionHeader title="Featured Community" />
            <div className="mb-6 grid grid-cols-1 gap-4 @[520px]:grid-cols-2">
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
            <div className="mb-6 grid grid-cols-1 gap-4 @[520px]:grid-cols-2">
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
            <div className="mb-6 grid grid-cols-1 gap-4 @[520px]:grid-cols-2 @[800px]:grid-cols-3">
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
                    <div className="mb-6 grid grid-cols-1 gap-4 @[520px]:grid-cols-2 @[800px]:grid-cols-3">
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

            {/* Create-room CTA — opens the modal */}
            <SectionHeader title="Start Your Own Room" />
            <div className="mb-10 flex flex-col items-start gap-3 rounded-3xl bg-white/70 dark:bg-slate-800/70 p-5 shadow-[0_8px_24px_-8px_rgba(46,92,202,0.25)] ring-1 ring-white/60 dark:ring-slate-700 backdrop-blur-xl">
                <p className="text-[13px] text-slate-700 dark:text-slate-300">
                    Spin up a fresh room with a title and an optional scenario
                    to set the scene.
                </p>
                <button
                    type="button"
                    onClick={onOpenCreate}
                    className="relative overflow-hidden rounded-full bg-gradient-to-br from-blue-500 to-indigo-600 px-5 py-2 text-sm font-semibold text-white shadow-[0_6px_14px_-4px_rgba(46,92,202,0.5)] ring-1 ring-white/50 ring-inset hover:from-blue-600 hover:to-indigo-700"
                >
                    <span
                        aria-hidden
                        className="pointer-events-none absolute inset-x-0 top-0 h-1/2 rounded-t-full bg-gradient-to-b from-white/35 to-transparent"
                    />
                    <span className="relative">+ Create Room</span>
                </button>
            </div>
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
