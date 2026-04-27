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
            style={{
                display: 'flex',
                alignItems: 'center',
                gap: 12,
                padding: '12px 14px',
                background: 'rgba(255,255,255,0.12)',
                border: '1px solid rgba(255,255,255,0.2)',
                borderRadius: 2,
                cursor: 'pointer',
                textAlign: 'left',
                transition: 'background 0.1s',
            }}
            onMouseEnter={(e) => {
                (e.currentTarget as HTMLButtonElement).style.background =
                    'rgba(255,255,255,0.22)';
            }}
            onMouseLeave={(e) => {
                (e.currentTarget as HTMLButtonElement).style.background =
                    'rgba(255,255,255,0.12)';
            }}
        >
            <span style={{ fontSize: 36, lineHeight: 1, flexShrink: 0 }}>
                {icon}
            </span>
            <div>
                <div
                    style={{
                        color: '#fff',
                        fontWeight: 700,
                        fontSize: 13,
                        marginBottom: 2,
                    }}
                >
                    {label}
                </div>
                <div style={{ color: 'rgba(255,255,255,0.75)', fontSize: 11 }}>
                    {description}
                </div>
            </div>
        </button>
    );
}
