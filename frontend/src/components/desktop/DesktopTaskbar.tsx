import {
    closeStartMenuWithTransition,
    Icon,
    minimiseWithTransition,
    openStartMenuWithTransition,
    restoreWithTransition,
    StartButton,
    Taskbar,
    TaskbarButton,
    Tray,
    TrayButton,
} from '@partytown/ui';
import { type ReactNode, useEffect, useState } from 'react';
import { useThemeStore } from '../../lib/theme-store';
import AppIcon from './AppIcon';

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
    tray?: ReactNode;
}

function useClock() {
    const [now, setNow] = useState<Date | null>(null);
    useEffect(() => {
        setNow(new Date());
        const id = setInterval(() => setNow(new Date()), 30_000);
        return () => clearInterval(id);
    }, []);
    return now;
}

export default function DesktopTaskbar({
    windows,
    focusedId,
    startMenuOpen,
    onToggle,
    onClose,
    onStartClick,
    tray,
}: DesktopTaskbarProps) {
    const mode = useThemeStore((s) => s.mode);
    const toggleTheme = useThemeStore((s) => s.toggle);
    const now = useClock();
    const themeLabel =
        mode === 'dark' ? 'Switch to light mode' : 'Switch to dark mode';

    const toggleWindow = (window: TaskbarWindowInfo) => {
        const update = () => onToggle(window.id);
        if (window.minimized) restoreWithTransition(window.id, update);
        else if (focusedId === window.id)
            minimiseWithTransition(window.id, update);
        else update();
    };

    return (
        <Taskbar
            className="fixed right-0 bottom-0 left-0 z-[99999]"
            start={
                <StartButton
                    open={startMenuOpen}
                    title="Start"
                    onClick={() =>
                        startMenuOpen
                            ? closeStartMenuWithTransition(onStartClick)
                            : openStartMenuWithTransition(onStartClick)
                    }
                />
            }
            tray={
                <Tray
                    clock={
                        <span title="Clock" suppressHydrationWarning>
                            {now
                                ? now.toLocaleTimeString([], {
                                      hour: '2-digit',
                                      minute: '2-digit',
                                  })
                                : ''}
                        </span>
                    }
                >
                    {tray}
                    <TrayButton
                        icon={<Icon name={mode === 'dark' ? 'sun' : 'moon'} />}
                        label={themeLabel}
                        onClick={toggleTheme}
                    />
                </Tray>
            }
        >
            {windows.map((window) => {
                const active = !window.minimized && focusedId === window.id;
                return (
                    <TaskbarButton
                        key={window.id}
                        taskId={window.id}
                        label={window.title}
                        title={window.title}
                        icon={<AppIcon icon={window.icon} />}
                        active={active}
                        minimised={window.minimized}
                        onClick={() => toggleWindow(window)}
                        onRestore={
                            window.minimized
                                ? () => toggleWindow(window)
                                : undefined
                        }
                        onMinimise={
                            active ? () => toggleWindow(window) : undefined
                        }
                        onClose={() => onClose(window.id)}
                    />
                );
            })}
        </Taskbar>
    );
}
