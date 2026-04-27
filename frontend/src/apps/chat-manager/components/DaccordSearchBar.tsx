interface DaccordSearchBarProps {
    value: string;
    onChange: (value: string) => void;
}

export default function DaccordSearchBar({
    value,
    onChange,
}: DaccordSearchBarProps) {
    return (
        <div className="relative w-full max-w-md">
            <span
                aria-hidden
                className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-white/80"
            >
                🔍
            </span>
            <input
                type="text"
                value={value}
                onChange={(e) => onChange(e.currentTarget.value)}
                placeholder="Search Explore"
                className="h-7 w-full rounded-full border border-white/30 bg-white/15 pl-8 pr-3 text-[12px] text-white placeholder:text-white/70 outline-none ring-0 backdrop-blur-sm focus:border-white/60 focus:bg-white/25"
            />
        </div>
    );
}
