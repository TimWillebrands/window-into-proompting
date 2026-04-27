export default function FormRow({
    label,
    children,
}: {
    label: string;
    children: React.ReactNode;
}) {
    return (
        <div
            style={{
                display: 'flex',
                flexDirection: 'column',
                gap: 4,
                marginBottom: 12,
            }}
        >
            <div
                style={{
                    color: '#1B5EAD',
                    fontSize: 12,
                    fontWeight: 600,
                }}
            >
                {label}
            </div>
            {children}
        </div>
    );
}
