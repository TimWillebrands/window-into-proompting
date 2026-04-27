interface RoomCardProps {
    name: string;
    description: string;
    emoji: string;
    online: number;
    members: number;
    /** Tailwind gradient classes (light + dark via `dark:` prefixes) */
    gradient: string;
    onClick?: () => void;
    disabled?: boolean;
    size?: 'lg' | 'md';
}

const fmt = (n: number) =>
    n >= 1000 ? `${(n / 1000).toFixed(n >= 10000 ? 0 : 1)}k` : `${n}`;

export default function RoomCard({
    name,
    description,
    emoji,
    online,
    members,
    gradient,
    onClick,
    disabled,
    size = 'lg',
}: RoomCardProps) {
    const isLg = size === 'lg';
    return (
        <button
            type="button"
            onClick={onClick}
            disabled={disabled}
            className={`group relative overflow-hidden rounded-2xl text-left text-white shadow-md transition-transform hover:-translate-y-0.5 hover:shadow-xl disabled:cursor-not-allowed disabled:hover:translate-y-0 disabled:hover:shadow-md bg-gradient-to-br ${gradient} ${
                isLg ? 'p-5 min-h-[180px]' : 'p-4 min-h-[120px]'
            } w-full`}
        >
            <span
                aria-hidden
                className="pointer-events-none absolute -right-10 -top-10 h-40 w-40 rounded-full bg-white/20 blur-2xl"
            />
            <span
                aria-hidden
                className="pointer-events-none absolute -left-12 bottom-0 h-32 w-32 rounded-full bg-white/10 blur-2xl"
            />
            <div className="relative flex h-full flex-col justify-between gap-3">
                <div className="flex items-start gap-3">
                    <div
                        className={`flex shrink-0 items-center justify-center rounded-full bg-white/30 backdrop-blur-sm ring-2 ring-white/40 ${
                            isLg ? 'h-14 w-14 text-3xl' : 'h-10 w-10 text-xl'
                        }`}
                    >
                        {emoji}
                    </div>
                    <div className="min-w-0 flex-1">
                        <div
                            className={`font-bold drop-shadow ${
                                isLg ? 'text-xl' : 'text-base'
                            }`}
                        >
                            {name}
                        </div>
                        <div
                            className={`mt-1 text-white/85 leading-snug ${
                                isLg ? 'text-sm' : 'text-xs line-clamp-2'
                            }`}
                        >
                            {description}
                        </div>
                    </div>
                </div>
                <div className="flex items-center justify-between text-xs">
                    <span className="inline-flex items-center gap-1.5 rounded-full bg-black/20 px-2 py-0.5 backdrop-blur-sm">
                        <span className="h-1.5 w-1.5 rounded-full bg-emerald-300 shadow-[0_0_4px_rgba(110,231,183,0.8)]" />
                        {fmt(online)} Online
                    </span>
                    <span className="inline-flex items-center gap-1.5 rounded-full bg-black/20 px-2 py-0.5 backdrop-blur-sm">
                        <span aria-hidden>👥</span>
                        {fmt(members)} Members
                    </span>
                </div>
            </div>
        </button>
    );
}
