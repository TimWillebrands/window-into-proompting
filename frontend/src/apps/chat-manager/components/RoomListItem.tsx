interface RoomListItemProps {
    name: string;
    sub?: string;
    emoji: string;
    /** Tailwind gradient classes incl. `dark:` */
    badgeGradient: string;
    onClick?: () => void;
    active?: boolean;
    online?: number;
}

export default function RoomListItem({
    name,
    sub,
    emoji,
    badgeGradient,
    onClick,
    active,
    online = 0,
}: RoomListItemProps) {
    const isLive = online > 0;
    return (
        <button
            type="button"
            onClick={onClick}
            className={`group flex w-full items-center gap-2.5 rounded-2xl px-2.5 py-2 text-left transition-all backdrop-blur-sm ${
                active
                    ? 'bg-gradient-to-br from-white to-blue-50 shadow-[inset_0_1px_0_rgba(255,255,255,0.7),0_4px_10px_-3px_rgba(46,92,202,0.3)] ring-1 ring-white dark:bg-slate-700/70'
                    : 'hover:bg-white/80 hover:shadow-[inset_0_1px_0_rgba(255,255,255,0.6),0_2px_6px_-2px_rgba(31,55,148,0.18)] dark:hover:bg-slate-700/40'
            }`}
        >
            <div
                className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-lg ring-2 ring-white/70 shadow-sm bg-gradient-to-br ${badgeGradient}`}
            >
                {emoji}
            </div>
            <div className="min-w-0 flex-1">
                <div className="truncate text-[13px] font-semibold text-slate-800 dark:text-slate-100">
                    {name}
                </div>
                {sub && (
                    <div className="flex items-center gap-1 truncate text-[11px] text-slate-500 dark:text-slate-400">
                        <span
                            className={`h-1.5 w-1.5 rounded-full ${
                                isLive
                                    ? 'bg-emerald-500 shadow-[0_0_4px_rgba(16,185,129,0.7)]'
                                    : 'bg-slate-400'
                            }`}
                        />
                        {sub}
                    </div>
                )}
            </div>
        </button>
    );
}
