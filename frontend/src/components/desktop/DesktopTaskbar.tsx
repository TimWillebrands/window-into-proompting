import { useEffect, useState } from 'react';
import { useThemeStore } from '../../lib/theme-store';

interface TaskbarWindowInfo {
    id: string;
    title: string | undefined;
    icon?: string;
    minimized?: boolean;
}

interface DesktopTaskbarProps {
    windows: TaskbarWindowInfo[];
    focusedId: string | null;
    startMenuOpen: boolean;
    onToggle: (id: string) => void;
    onClose: (id: string) => void;
    onStartClick: () => void;
}

function useClock() {
    const [now, setNow] = useState(() => new Date());
    useEffect(() => {
        const tick = () => setNow(new Date());
        const id = setInterval(tick, 30_000);
        return () => clearInterval(id);
    }, []);
    return now;
}

function isImagePath(s: string | undefined) {
    return (
        !!s && (/\.(png|jpe?g|webp|svg|ico|gif)$/i.test(s) || s.startsWith('/'))
    );
}

export default function DesktopTaskbar({
    windows,
    focusedId,
    startMenuOpen,
    onToggle,
    onClose,
    onStartClick,
}: DesktopTaskbarProps) {
    const mode = useThemeStore((s) => s.mode);
    const toggleTheme = useThemeStore((s) => s.toggle);
    const now = useClock();

    return (
        <div className="xp-taskbar fixed bottom-0 left-0 right-0 h-[30px] z-[99999] flex items-stretch text-white">
            {/* Start button (spritesheet bg, real "start" text only for screen readers) */}
            <button
                type="button"
                data-start-button
                data-selected={startMenuOpen ? 'true' : 'false'}
                aria-pressed={startMenuOpen}
                aria-label="Start"
                title="Start"
                onClick={onStartClick}
                className="xp-start-btn"
            >
                start
            </button>

            {/* Window tabs */}
            <ul className="flex-1 flex items-center gap-1 overflow-hidden py-[3px] px-[7px] m-0 list-none">
                {windows.map((window) => {
                    const isFocused =
                        !window.minimized && focusedId === window.id;
                    return (
                        <li
                            key={window.id}
                            className={`xp-task-item ${isFocused ? 'xp-active' : ''}`}
                            data-active={isFocused ? 'true' : 'false'}
                            title={window.title}
                        >
                            <button
                                type="button"
                                onClick={() => onToggle(window.id)}
                                className="flex items-center gap-1.5 flex-1 min-w-0 bg-transparent border-0 p-0 text-inherit text-[11px] cursor-pointer"
                            >
                                {window.icon &&
                                    (isImagePath(window.icon) ? (
                                        <img
                                            src={window.icon}
                                            alt=""
                                            aria-hidden
                                            className="w-[16px] h-[16px] shrink-0 object-contain"
                                            style={{
                                                imageRendering: 'pixelated',
                                            }}
                                        />
                                    ) : (
                                        <span
                                            aria-hidden
                                            className="text-[13px] shrink-0"
                                        >
                                            {window.icon}
                                        </span>
                                    ))}
                                <span className="truncate flex-1 text-left">
                                    {window.title}
                                </span>
                            </button>
                            <button
                                type="button"
                                aria-label={`Close ${window.title ?? 'window'}`}
                                onClick={() => onClose(window.id)}
                                className="ml-1 h-4 w-4 flex items-center justify-center text-[10px] rounded hover:bg-black/30 shrink-0 bg-transparent border-0 text-white cursor-pointer"
                            >
                                ✕
                            </button>
                        </li>
                    );
                })}
            </ul>

            {/* System tray */}
            <div className="xp-tray ml-auto">
                <button
                    type="button"
                    aria-label={
                        mode === 'dark'
                            ? 'Switch to light mode'
                            : 'Switch to dark mode'
                    }
                    title={
                        mode === 'dark'
                            ? 'Switch to light mode'
                            : 'Switch to dark mode'
                    }
                    onClick={toggleTheme}
                    className="h-5 w-5 flex items-center justify-center rounded hover:bg-white/20 text-base leading-none cursor-default bg-transparent border-0 text-white"
                >
                    {mode === 'dark' ? '☀️' : '🌙'}
                </button>
                <span title="Clock" className="tabular-nums select-none">
                    {now.toLocaleTimeString([], {
                        hour: '2-digit',
                        minute: '2-digit',
                    })}
                </span>
            </div>
        </div>
    );
}
