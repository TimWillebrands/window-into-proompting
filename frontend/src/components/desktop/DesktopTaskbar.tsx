interface DesktopTaskbarProps {
    windows: Array<{ id: string; title: string | undefined; icon?: string }>;
    focusedId: string | null;
    onToggle: (id: string) => void;
    onClose: (id: string) => void;
}

export default function DesktopTaskbar({
    windows,
    focusedId,
    onToggle,
    onClose,
}: DesktopTaskbarProps) {
    return (
        <div className="fixed bottom-0 left-0 right-0 h-10 bg-gradient-to-b from-blue-600 to-blue-800 border-t border-blue-300 flex items-center px-2 gap-2 z-[99999]">
            <button type="button">🪟 start</button>
            {windows.map((window) => (
                <div
                    key={window.id}
                    className="flex items-center gap-0.5"
                >
                    <button
                        type="button"
                        aria-pressed={focusedId === window.id}
                        onClick={() => onToggle(window.id)}
                        className="flex items-center max-w-[180px]"
                    >
                        {window.icon && (
                            <span className="mr-1">{window.icon}</span>
                        )}
                        <span className="truncate">{window.title}</span>
                    </button>
                    <div className="title-bar-controls">
                        <button
                            type="button"
                            aria-label="Close"
                            title={`Close ${window.title ?? 'window'}`}
                            onClick={() => onClose(window.id)}
                        />
                    </div>
                </div>
            ))}
        </div>
    );
}
