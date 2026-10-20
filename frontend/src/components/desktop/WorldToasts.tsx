import { useWorldToasts } from '../../lib/world';

/** XP tray balloons above the taskbar: someone in the World reached out to you. */
export default function WorldToasts() {
    const toasts = useWorldToasts((s) => s.toasts);
    const dismiss = useWorldToasts((s) => s.dismiss);

    return (
        <div className="pointer-events-none fixed right-3 bottom-10 z-[9999] flex w-72 flex-col gap-2">
            {toasts.map((t) => (
                <div
                    key={t.id}
                    className="window pointer-events-auto shadow-lg"
                    aria-live="polite"
                >
                    <div className="title-bar">
                        <div className="title-bar-text">
                            {t.icon ? `${t.icon} ` : ''}
                            {t.title}
                        </div>
                        <div className="title-bar-controls">
                            <button
                                type="button"
                                aria-label="Close"
                                onClick={() => dismiss(t.id)}
                            />
                        </div>
                    </div>
                    <button
                        type="button"
                        className="window-body m-0 w-full cursor-pointer border-0 bg-transparent p-2 text-left text-xs"
                        onClick={() => {
                            t.onOpen?.();
                            dismiss(t.id);
                        }}
                    >
                        {t.body}
                    </button>
                </div>
            ))}
        </div>
    );
}
