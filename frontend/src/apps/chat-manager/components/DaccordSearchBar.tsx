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
                className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-white/85"
            >
                🔍
            </span>
            <input
                type="text"
                value={value}
                onChange={(e) => onChange(e.currentTarget.value)}
                placeholder="Search Explore"
                className="h-7 w-full rounded-full bg-white/20 pl-8 pr-3 text-[12px] text-white placeholder:text-white/75 outline-none ring-1 ring-white/30 backdrop-blur-md transition-colors focus:bg-white/30 focus:ring-white/70"
            />
        </div>
    );
}
