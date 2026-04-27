import { useState } from 'react';
import RoomListItem from './RoomListItem';
import {
    NAV_CATEGORIES,
    PROFILE_STUB,
    paletteFor,
    SERVER_RAIL,
} from './stub-data';

interface RealRoom {
    id: string;
    name: string;
    online?: number | null;
}

interface DaccordSidebarProps {
    realRooms: RealRoom[];
    onSelectRoom: (id: string) => void;
    activeCategory: string;
    onSelectCategory: (id: string) => void;
}

export default function DaccordSidebar({
    realRooms,
    onSelectRoom,
    activeCategory,
    onSelectCategory,
}: DaccordSidebarProps) {
    const [yourRoomsOpen, setYourRoomsOpen] = useState(true);

    return (
        <aside className="flex h-full w-[300px] shrink-0 flex-col bg-gradient-to-b from-indigo-200 via-sky-100 to-purple-100 dark:from-indigo-950 dark:via-slate-900 dark:to-purple-950 text-slate-800 dark:text-slate-100">
            <div className="flex flex-1 min-h-0">
                {/* Server rail (the leftmost narrow column with circle icons) */}
                <div className="flex w-12 shrink-0 flex-col items-center gap-2 py-3">
                    {SERVER_RAIL.map((s) => (
                        <button
                            key={s.id}
                            type="button"
                            title={s.label}
                            className="flex h-9 w-9 items-center justify-center rounded-full bg-white/40 text-base shadow-sm transition hover:bg-white/70 hover:scale-110 dark:bg-slate-800/60 dark:hover:bg-slate-700"
                        >
                            <span aria-hidden>{s.icon}</span>
                        </button>
                    ))}
                </div>

                {/* Categories + Your Rooms */}
                <div className="flex flex-1 min-w-0 flex-col gap-1 overflow-y-auto px-2 py-3">
                    <nav className="flex flex-col gap-0.5">
                        {NAV_CATEGORIES.map((c) => {
                            const active = c.id === activeCategory;
                            return (
                                <button
                                    key={c.id}
                                    type="button"
                                    onClick={() => onSelectCategory(c.id)}
                                    className={`flex items-center gap-2 rounded-xl px-3 py-2 text-[13px] font-medium transition-colors ${
                                        active
                                            ? 'bg-blue-500 text-white shadow'
                                            : 'text-slate-700 hover:bg-white/60 dark:text-slate-200 dark:hover:bg-slate-800/60'
                                    }`}
                                >
                                    <span className="text-base" aria-hidden>
                                        {c.icon}
                                    </span>
                                    {c.label}
                                </button>
                            );
                        })}
                        <button
                            type="button"
                            disabled
                            className="mt-1 flex items-center gap-2 rounded-xl px-3 py-2 text-[13px] font-medium text-slate-500 dark:text-slate-400 opacity-70 cursor-not-allowed"
                        >
                            <span className="text-base" aria-hidden>
                                ➕
                            </span>
                            Create Hub
                        </button>
                    </nav>

                    <div className="mt-4">
                        <button
                            type="button"
                            onClick={() => setYourRoomsOpen((v) => !v)}
                            className="flex w-full items-center justify-between px-2 text-[12px] font-bold uppercase tracking-wider text-slate-600 dark:text-slate-300"
                        >
                            <span>Your Rooms</span>
                            <span aria-hidden>{yourRoomsOpen ? '▾' : '▸'}</span>
                        </button>
                        {yourRoomsOpen && (
                            <div className="mt-1 flex flex-col gap-0.5">
                                {realRooms.length === 0 ? (
                                    <p className="px-3 py-2 text-[11px] text-slate-500 dark:text-slate-400">
                                        No rooms yet. Create one in Discover or
                                        the prompt below.
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
                                        className="mt-1 px-3 py-1 text-left text-[11px] font-semibold text-blue-600 dark:text-blue-400 hover:underline"
                                    >
                                        See all rooms
                                    </button>
                                )}
                            </div>
                        )}
                    </div>
                </div>
            </div>

            {/* Profile footer */}
            <div className="flex items-center gap-2 border-t border-white/40 dark:border-slate-700 bg-white/40 dark:bg-slate-900/60 p-2.5 backdrop-blur-sm">
                <div className="relative shrink-0">
                    <img
                        alt={PROFILE_STUB.name}
                        src={`https://robohash.org/${PROFILE_STUB.avatarSeed}.png?size=40x40`}
                        className="h-9 w-9 rounded-full ring-2 ring-white dark:ring-slate-700 bg-white"
                    />
                    <span
                        className={`absolute -right-0 -bottom-0 h-2.5 w-2.5 rounded-full ring-2 ring-white dark:ring-slate-900 ${
                            PROFILE_STUB.online
                                ? 'bg-emerald-500'
                                : 'bg-slate-400'
                        }`}
                    />
                </div>
                <div className="min-w-0 flex-1">
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
                    className="rounded-full p-1.5 text-slate-600 hover:bg-white/70 dark:text-slate-300 dark:hover:bg-slate-700"
                >
                    🎤
                </button>
                <button
                    type="button"
                    title="Headphones"
                    className="rounded-full p-1.5 text-slate-600 hover:bg-white/70 dark:text-slate-300 dark:hover:bg-slate-700"
                >
                    🎧
                </button>
                <button
                    type="button"
                    title="Settings"
                    className="rounded-full p-1.5 text-slate-600 hover:bg-white/70 dark:text-slate-300 dark:hover:bg-slate-700"
                >
                    ⚙️
                </button>
            </div>
        </aside>
    );
}
