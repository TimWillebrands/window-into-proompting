import { useEffect, useRef } from 'react';
import { DESKTOP_ICONS, WINDOW_PRESETS } from '../../lib/window-presets';

interface StartMenuProps {
    onLaunch: (windowId: string) => void;
    onClose: () => void;
}

export default function StartMenu({ onLaunch, onClose }: StartMenuProps) {
    const rootRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        const handlePointer = (event: PointerEvent) => {
            const target = event.target as Node | null;
            if (!rootRef.current || !target) return;
            if (rootRef.current.contains(target)) return;
            if (
                target instanceof Element &&
                target.closest('[data-start-button]')
            ) {
                return;
            }
            onClose();
        };
        const handleKey = (event: KeyboardEvent) => {
            if (event.key === 'Escape') onClose();
        };
        document.addEventListener('pointerdown', handlePointer);
        document.addEventListener('keydown', handleKey);
        return () => {
            document.removeEventListener('pointerdown', handlePointer);
            document.removeEventListener('keydown', handleKey);
        };
    }, [onClose]);

    const items = DESKTOP_ICONS.map((icon) => {
        const preset = WINDOW_PRESETS.find((p) => p.id === icon.windowId);
        return {
            ...icon,
            title: preset?.title ?? icon.label,
        };
    });

    // Pinned (left) vs system (right) split — react-xp shows the pinned column on the left and a "system menu" panel on the right.
    const pinned = items;

    return (
        <div
            ref={rootRef}
            className="xp-startmenu absolute bottom-[30px] left-0 z-[100000]"
        >
            <header className="xp-startmenu-header">
                <span
                    className="xp-startmenu-avatar"
                    aria-hidden
                    role="presentation"
                >
                    👤
                </span>
                <h1>Partytown User</h1>
            </header>

            <main className="xp-startmenu-body">
                <section className="py-1">
                    {pinned.map((item) => (
                        <button
                            key={item.id}
                            type="button"
                            className="xp-startmenu-item"
                            onClick={() => {
                                onLaunch(item.windowId);
                                onClose();
                            }}
                        >
                            <img
                                src={item.icon}
                                alt=""
                                aria-hidden
                                className="w-7 h-7 shrink-0 object-contain"
                                style={{ imageRendering: 'pixelated' }}
                            />
                            <span className="flex flex-col leading-tight">
                                <span className="font-semibold">
                                    {item.title}
                                </span>
                                <span className="text-[10px] opacity-70">
                                    Open
                                </span>
                            </span>
                        </button>
                    ))}
                </section>

                <section className="xp-startmenu-section--system py-1">
                    <button
                        type="button"
                        className="xp-startmenu-item"
                        onClick={onClose}
                    >
                        <span className="text-lg" aria-hidden>
                            ❓
                        </span>
                        <span>Help and Support</span>
                    </button>
                    <button
                        type="button"
                        className="xp-startmenu-item"
                        onClick={onClose}
                    >
                        <span className="text-lg" aria-hidden>
                            🔍
                        </span>
                        <span>Search</span>
                    </button>
                    <button
                        type="button"
                        className="xp-startmenu-item"
                        onClick={onClose}
                    >
                        <span className="text-lg" aria-hidden>
                            🏃
                        </span>
                        <span>Run...</span>
                    </button>
                </section>
            </main>

            <footer className="xp-startmenu-footer">
                <span>v0.1</span>
                <button
                    type="button"
                    className="xp-startmenu-item w-auto px-3 py-1 rounded bg-white/10 hover:bg-white/25"
                    onClick={onClose}
                >
                    <span aria-hidden>⏻</span>
                    <span>Close</span>
                </button>
            </footer>
        </div>
    );
}
