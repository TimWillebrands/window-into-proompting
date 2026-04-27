import { useState } from 'react';
import RoomListItem from './RoomListItem';
import {
    NAV_CATEGORIES,
    PROFILE_STUB,
    paletteFor,
    SERVER_RAIL,
} from './stub-data';
import WaveDecoration from './WaveDecoration';

interface RealRoom {
    id: string;
    name: string;
    online?: number | null;
}

interface DaccordSidebarProps {
    realRooms: RealRoom[];
    onSelectRoom: (id: string) => void;
    activeRoomId?: string;
    activeCategory: string;
    onSelectCategory: (id: string) => void;
}

export default function DaccordSidebar({
    realRooms,
    onSelectRoom,
    activeRoomId,
    activeCategory,
    onSelectCategory,
}: DaccordSidebarProps) {
    const [yourRoomsOpen, setYourRoomsOpen] = useState(true);

    return (
        <aside className="relative flex h-full w-14 @[640px]:w-[300px] shrink-0 flex-col overflow-hidden bg-gradient-to-b from-blue-600 via-indigo-600 to-purple-700 dark:from-indigo-950 dark:via-slate-900 dark:to-purple-950 text-slate-800 dark:text-slate-100 p-1.5 @[640px]:p-2 gap-2">
            <WaveDecoration variant="panel" />

            <div className="relative flex flex-1 min-h-0 gap-2">
                {/* Server rail — narrow strip on saturated blue */}
                <div className="flex w-11 shrink-0 flex-col items-center gap-2.5 py-2">
                    {SERVER_RAIL.map((s) => (
                        <button
                            key={s.id}
                            type="button"
                            title={s.label}
                            className="relative flex h-11 w-11 items-center justify-center rounded-full bg-gradient-to-br from-white/35 to-white/10 text-2xl ring-2 ring-white/40 shadow-[inset_0_1px_0_rgba(255,255,255,0.5),0_4px_10px_-2px_rgba(0,0,0,0.3)] backdrop-blur-md transition-all duration-150 hover:from-white/55 hover:to-white/25 hover:scale-110 hover:ring-white/70"
                        >
                            <span aria-hidden>{s.icon}</span>
                        </button>
                    ))}
                </div>

                {/* Floating glassy plate — categories + your rooms */}
                <div className="relative hidden @[640px]:flex flex-1 min-w-0 flex-col gap-1 overflow-hidden rounded-3xl bg-gradient-to-b from-white/85 to-white/70 dark:from-slate-800/80 dark:to-slate-900/70 px-2 py-3 shadow-[0_10px_30px_-8px_rgba(31,55,148,0.4)] ring-1 ring-white/70 backdrop-blur-xl">
                    <span
                        aria-hidden
                        className="pointer-events-none absolute inset-x-0 top-0 h-20 rounded-t-3xl bg-gradient-to-b from-white/60 to-transparent"
                    />
                    <div className="flex flex-1 min-h-0 flex-col gap-1 overflow-y-auto pr-1">
                        <nav className="flex flex-col gap-0.5">
                            {NAV_CATEGORIES.map((c) => {
                                const active = c.id === activeCategory;
                                return (
                                    <button
                                        key={c.id}
                                        type="button"
                                        onClick={() => onSelectCategory(c.id)}
                                        className={`flex items-center gap-2.5 rounded-2xl px-3 py-2 text-[13px] font-medium transition-all ${
                                            active
                                                ? 'bg-gradient-to-br from-blue-500 to-indigo-600 text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.4),0_4px_10px_-3px_rgba(46,92,202,0.5)] ring-1 ring-white/50 ring-inset'
                                                : 'text-slate-700 hover:bg-white/80 hover:shadow-sm dark:text-slate-200 dark:hover:bg-slate-700/60'
                                        }`}
                                    >
                                        <span className="text-lg" aria-hidden>
                                            {c.icon}
                                        </span>
                                        {c.label}
                                    </button>
                                );
                            })}
                            <button
                                type="button"
                                disabled
                                title="Create Hub"
                                aria-label="Create Hub"
                                className="relative mt-2 flex h-10 w-10 items-center justify-center self-start overflow-hidden rounded-full bg-gradient-to-br from-blue-400 via-blue-500 to-indigo-600 text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.5),0_4px_10px_-2px_rgba(46,92,202,0.5)] ring-2 ring-white/60 opacity-90 cursor-not-allowed"
                            >
                                <span
                                    className="text-lg leading-none"
                                    aria-hidden
                                >
                                    +
                                </span>
                            </button>
                        </nav>

                        <div className="mt-4">
                            <button
                                type="button"
                                onClick={() => setYourRoomsOpen((v) => !v)}
                                className="flex w-full items-center justify-between px-2 text-[12px] font-bold uppercase tracking-wider text-slate-700 dark:text-slate-300"
                            >
                                <span>Your Rooms</span>
                                <span aria-hidden>
                                    {yourRoomsOpen ? '▾' : '▸'}
                                </span>
                            </button>
                            {yourRoomsOpen && (
                                <div className="mt-1 flex flex-col gap-0.5">
                                    {realRooms.length === 0 ? (
                                        <p className="px-3 py-2 text-[11px] text-slate-500 dark:text-slate-400">
                                            No rooms yet. Create one in Discover
                                            or the prompt below.
                                        </p>
                                    ) : (
                                        realRooms.slice(0, 6).map((r) => {
                                            const palette = paletteFor(r.id);
                                            const sub =
                                                r.online != null
                                                    ? `${r.online} online`
                                                    : 'idle';
                                            return (
                                                <RoomListItem
                                                    key={r.id}
                                                    name={r.name}
                                                    sub={sub}
                                                    emoji={palette.emoji}
                                                    badgeGradient={
                                                        palette.badgeGradient
                                                    }
                                                    online={r.online ?? 0}
                                                    active={
                                                        r.id === activeRoomId
                                                    }
                                                    onClick={() =>
                                                        onSelectRoom(r.id)
                                                    }
                                                />
                                            );
                                        })
                                    )}
                                    {realRooms.length > 6 && (
                                        <button
                                            type="button"
                                            className="mt-1 px-3 py-1 text-left text-[11px] font-semibold text-blue-700 dark:text-blue-400 hover:underline"
                                        >
                                            See all rooms
                                        </button>
                                    )}
                                </div>
                            )}
                        </div>
                    </div>
                </div>
            </div>

            {/* Profile dock — floating glassy card at bottom */}
            <div className="relative flex items-center gap-2 overflow-hidden rounded-3xl bg-white/85 dark:bg-slate-800/80 p-2 shadow-[0_10px_24px_-8px_rgba(31,55,148,0.4)] ring-1 ring-white/70 backdrop-blur-xl">
                <span
                    aria-hidden
                    className="pointer-events-none absolute inset-x-0 top-0 h-1/2 rounded-t-3xl bg-gradient-to-b from-white/55 to-transparent"
                />
                <div className="relative shrink-0">
                    <img
                        alt={PROFILE_STUB.name}
                        src={`https://robohash.org/${PROFILE_STUB.avatarSeed}.png?size=80x80`}
                        className="h-10 w-10 rounded-full ring-2 ring-white dark:ring-slate-700 bg-white shadow-sm"
                    />
                    <span
                        className={`absolute -right-0 -bottom-0 h-2.5 w-2.5 rounded-full ring-2 ring-white dark:ring-slate-900 ${
                            PROFILE_STUB.online
                                ? 'bg-emerald-500 shadow-[0_0_6px_rgba(16,185,129,0.8)]'
                                : 'bg-slate-400'
                        }`}
                    />
                </div>
                <div className="hidden @[640px]:block min-w-0 flex-1">
                    <div className="truncate text-[13px] font-semibold text-slate-800 dark:text-slate-100">
                        {PROFILE_STUB.name}
                    </div>
                    <div className="flex items-center gap-1 text-[11px] text-slate-500 dark:text-slate-400">
                        <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
                        Online
                    </div>
                </div>
                <button
                    type="button"
                    title="Microphone"
                    className="hidden @[640px]:inline-flex rounded-full p-1.5 text-slate-600 hover:bg-blue-500/10 dark:text-slate-300 dark:hover:bg-slate-700"
                >
                    🎤
                </button>
                <button
                    type="button"
                    title="Headphones"
                    className="hidden @[640px]:inline-flex rounded-full p-1.5 text-slate-600 hover:bg-blue-500/10 dark:text-slate-300 dark:hover:bg-slate-700"
                >
                    🎧
                </button>
                <button
                    type="button"
                    title="Settings"
                    className="hidden @[640px]:inline-flex rounded-full p-1.5 text-slate-600 hover:bg-blue-500/10 dark:text-slate-300 dark:hover:bg-slate-700"
                >
                    ⚙️
                </button>
            </div>
        </aside>
    );
}
