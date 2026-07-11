export default function CategoryTile({
    icon,
    label,
    description,
    onClick,
}: {
    icon: string;
    label: string;
    description: string;
    onClick: () => void;
}) {
    return (
        <button
            type="button"
            onClick={onClick}
            className="xp-bare group flex items-center gap-3 rounded-2xl px-3.5 py-3 text-left cursor-pointer bg-white/15 ring-1 ring-white/30 shadow-[inset_0_1px_0_rgba(255,255,255,0.35)] backdrop-blur-md transition-all duration-150 hover:bg-white/25 hover:ring-white/50 hover:-translate-y-px hover:shadow-[inset_0_1px_0_rgba(255,255,255,0.45),0_6px_14px_-4px_rgba(0,0,0,0.3)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-white/80"
        >
            <span
                aria-hidden
                style={{ fontSize: 36, lineHeight: 1, flexShrink: 0 }}
            >
                {icon}
            </span>
            <div>
                <div
                    style={{
                        color: '#fff',
                        fontWeight: 700,
                        fontSize: 13,
                        marginBottom: 2,
                        textShadow: '0 1px 2px rgba(0,0,0,0.25)',
                    }}
                >
                    {label}
                </div>
                <div style={{ color: 'rgba(255,255,255,0.8)', fontSize: 11 }}>
                    {description}
                </div>
            </div>
        </button>
    );
}
