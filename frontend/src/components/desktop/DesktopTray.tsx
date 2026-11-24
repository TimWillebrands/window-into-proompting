import { Icon, Tray, TrayButton } from '@partytown/ui';
import { type ReactNode, useEffect, useState } from 'react';
import { useThemeStore } from '../../lib/theme-store';

function useClock() {
    const [now, setNow] = useState<Date | null>(null);
    useEffect(() => {
        setNow(new Date());
        const id = setInterval(() => setNow(new Date()), 30_000);
        return () => clearInterval(id);
    }, []);
    return now;
}

export default function DesktopTray({ children }: { children?: ReactNode }) {
    const mode = useThemeStore((s) => s.mode);
    const toggleTheme = useThemeStore((s) => s.toggle);
    const now = useClock();
    const themeLabel =
        mode === 'dark' ? 'Switch to light mode' : 'Switch to dark mode';

    return (
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
            {children}
            <TrayButton
                icon={<Icon name={mode === 'dark' ? 'sun' : 'moon'} />}
                label={themeLabel}
                onClick={toggleTheme}
            />
        </Tray>
    );
}
