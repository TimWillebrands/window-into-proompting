import WaveDecoration from './WaveDecoration';

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
            title={
                disabled
                    ? 'Example room — joining is not available yet'
                    : undefined
            }
            className={`group relative overflow-hidden rounded-[28px] text-left text-white shadow-[0_8px_24px_-6px_rgba(31,55,148,0.35),0_2px_6px_-2px_rgba(0,0,0,0.2)] ring-1 ring-white/40 transition-all duration-200 hover:-translate-y-0.5 hover:shadow-[0_18px_36px_-8px_rgba(31,55,148,0.45),0_4px_10px_-2px_rgba(0,0,0,0.25)] hover:saturate-125 disabled:cursor-not-allowed disabled:hover:translate-y-0 disabled:hover:shadow-[0_8px_24px_-6px_rgba(31,55,148,0.35),0_2px_6px_-2px_rgba(0,0,0,0.2)] disabled:hover:saturate-100 bg-gradient-to-br ${gradient} ${
                isLg ? 'p-5 min-h-[180px]' : 'p-4 min-h-[120px]'
            } w-full`}
        >
            <span
                aria-hidden
                className="pointer-events-none absolute -right-10 -top-10 h-40 w-40 rounded-full bg-white/30 blur-2xl"
            />
            {/* Legibility overlay — keeps white text readable across any gradient */}
            <span
                aria-hidden
                className="pointer-events-none absolute inset-0 bg-gradient-to-b from-black/15 via-black/30 to-black/55"
            />
            {/* XP-glass top sheen */}
            <span
                aria-hidden
                className="pointer-events-none absolute inset-x-0 top-0 h-1/2 rounded-t-[28px] bg-gradient-to-b from-white/35 via-white/10 to-transparent"
            />
            <WaveDecoration variant="card" />
            <div className="relative flex h-full flex-col justify-between gap-3">
                <div className="flex items-start gap-3">
                    <div
                        className={`flex shrink-0 items-center justify-center rounded-full bg-white/85 text-slate-800 backdrop-blur-sm ring-2 ring-white shadow-inner ${
                            isLg ? 'h-16 w-16 text-3xl' : 'h-11 w-11 text-xl'
                        }`}
                    >
                        {emoji}
                    </div>
                    <div className="min-w-0 flex-1">
                        <div
                            className={`font-bold drop-shadow-[0_2px_4px_rgba(0,0,0,0.5)] ${
                                isLg ? 'text-xl' : 'text-base'
                            }`}
                        >
                            {name}
                        </div>
                        <div
                            className={`mt-1 font-medium text-white leading-snug drop-shadow-[0_1px_3px_rgba(0,0,0,0.6)] ${
                                isLg ? 'text-sm' : 'text-xs line-clamp-2'
                            }`}
                        >
                            {description}
                        </div>
                    </div>
                </div>
                <div className="flex items-center justify-between text-xs">
                    <span className="inline-flex items-center gap-1.5 rounded-full bg-black/25 px-2 py-0.5 ring-1 ring-white/20 backdrop-blur-sm">
                        <span className="h-1.5 w-1.5 rounded-full bg-emerald-300 shadow-[0_0_4px_rgba(110,231,183,0.9)]" />
                        {fmt(online)} Online
                    </span>
                    <span className="inline-flex items-center gap-1.5 rounded-full bg-black/25 px-2 py-0.5 ring-1 ring-white/20 backdrop-blur-sm">
                        <span aria-hidden>👥</span>
                        {fmt(members)} Members
                    </span>
                </div>
            </div>
        </button>
    );
}
