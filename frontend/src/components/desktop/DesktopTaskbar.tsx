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
            <button
                type="button"
                className="px-4 h-7 text-white font-semibold bg-gradient-to-b! from-green-400 to-green-600 rounded"
            >
                🪟 start
            </button>
            {windows.map((window) => (
                <div
                    key={window.id}
                    role="button"
                    tabIndex={0}
                    className={`h-7 px-3 text-sm rounded border relative pr-8 cursor-default select-none flex items-center ${
                        focusedId === window.id
                            ? 'bg-orange-200 text-black'
                            : 'bg-blue-100 text-black'
                    }`}
                    onClick={() => onToggle(window.id)}
                    onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                            e.preventDefault();
                            onToggle(window.id);
                        }
                    }}
                >
                    {window.icon && <span className="mr-1">{window.icon}</span>}
                    <span className="truncate">{window.title}</span>
                    <button
                        type="button"
                        aria-label={`Close ${window.title ?? 'window'}`}
                        className="absolute min-w-0! min-h-0! right-1 top-1/2 -translate-y-1/2 text-xs px-1! hover:bg-black/10 rounded"
                        onClick={(event) => {
                            event.stopPropagation();
                            onClose(window.id);
                        }}
                    >
                        ✕
                    </button>
                </div>
            ))}
        </div>
    );
}
