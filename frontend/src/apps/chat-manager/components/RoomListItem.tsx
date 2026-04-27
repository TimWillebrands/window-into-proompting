interface RoomListItemProps {
    name: string;
    sub?: string;
    emoji: string;
    /** Tailwind gradient classes incl. `dark:` */
    badgeGradient: string;
    onClick?: () => void;
    active?: boolean;
}

export default function RoomListItem({
    name,
    sub,
    emoji,
    badgeGradient,
    onClick,
    active,
}: RoomListItemProps) {
    return (
        <button
            type="button"
            onClick={onClick}
            className={`group flex w-full items-center gap-2.5 rounded-xl px-2 py-1.5 text-left transition-colors ${
                active
                    ? 'bg-white/70 dark:bg-slate-700/70'
                    : 'hover:bg-white/40 dark:hover:bg-slate-700/40'
            }`}
        >
            <div
                className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-lg ring-1 ring-white/40 bg-gradient-to-br ${badgeGradient}`}
            >
                {emoji}
            </div>
            <div className="min-w-0 flex-1">
                <div className="truncate text-[13px] font-semibold text-slate-800 dark:text-slate-100">
                    {name}
                </div>
                {sub && (
                    <div className="flex items-center gap-1 truncate text-[11px] text-slate-500 dark:text-slate-400">
                        <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
                        {sub}
                    </div>
                )}
            </div>
        </button>
    );
}
