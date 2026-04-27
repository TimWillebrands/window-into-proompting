import WindowFrame from '../../../components/desktop/WindowFrame';

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
            className="fixed inset-0 z-[1000] bg-black/50 flex items-center justify-center"
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
                className="w-[480px] max-h-[85vh] flex flex-col app-surface"
                onClick={(e) => e.stopPropagation()}
                onKeyDown={(e) => e.stopPropagation()}
            >
                <WindowFrame
                    id={`provider-modal-${title}`}
                    title={title}
                    icon="🛠️"
                    width={480}
                    height={0}
                    zIndex={1000}
                    focused
                    onMinimize={onClose}
                    onClose={onClose}
                >
                    <div className="overflow-y-auto p-3">{children}</div>
                </WindowFrame>
            </div>
        </div>
    );
}
