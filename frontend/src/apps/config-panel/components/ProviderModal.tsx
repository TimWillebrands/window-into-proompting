export default function ProviderModal({
    title,
    onClose,
    children,
}: {
    title: string;
    onClose: () => void;
    children: React.ReactNode;
}) {
    return (
        // biome-ignore lint/a11y/noStaticElementInteractions: backdrop click dismisses modal; real dialog is inside
        <div
            role="presentation"
            style={{
                position: 'fixed',
                inset: 0,
                zIndex: 1000,
                background: 'rgba(0,0,0,0.5)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
            }}
            onClick={onClose}
            onKeyDown={(e) => {
                if (e.key === 'Escape') {
                    onClose();
                }
            }}
        >
            <div
                role="dialog"
                aria-label={title}
                className="window"
                style={{
                    width: 480,
                    maxHeight: '85vh',
                    display: 'flex',
                    flexDirection: 'column',
                }}
                onClick={(e) => e.stopPropagation()}
                onKeyDown={(e) => e.stopPropagation()}
            >
                <div className="title-bar box-content">
                    <div className="title-bar-text">{title}</div>
                    <div className="title-bar-controls">
                        <button
                            type="button"
                            aria-label="Close"
                            onClick={onClose}
                        />
                    </div>
                </div>
                <div
                    className="window-body"
                    style={{
                        overflowY: 'auto',
                    }}
                >
                    {children}
                </div>
            </div>
        </div>
    );
}
