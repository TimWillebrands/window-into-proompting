import { Desktop, DesktopIcon, type MenuEntry } from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import AppIcon from '../components/desktop/AppIcon';
import ClerkPlayerBridge from '../components/desktop/ClerkPlayerBridge';
import CreateCharacterScreen from '../components/desktop/CreateCharacterScreen';
import DesktopTray from '../components/desktop/DesktopTray';
import DesktopWallpaper from '../components/desktop/DesktopWallpaper';
import NotificationBanners from '../components/desktop/notifications/NotificationBanners';
import NotificationCenter from '../components/desktop/notifications/NotificationCenter';
import NotificationTray from '../components/desktop/notifications/NotificationTray';
import StartMenu from '../components/desktop/StartMenu';
import WelcomeScreen from '../components/desktop/WelcomeScreen';
import { DesktopProvider, useDesktopStore } from '../lib/desktop-context';
import { desktopPrograms, PROGRAMS } from '../lib/window-presets';
import {
    useCanOpen,
    useForgetStoppedWorld,
    usePlayerAccount,
    usePlayerSession,
    useWorldChoice,
    WorldRealtimeBridge,
} from '../lib/world';

const renderIcon = (icon: string, size: 16 | 32 | 48) => (
    <AppIcon icon={icon} size={size} />
);

function DesktopWindowLayer() {
    const canOpen = useCanOpen();
    const desktopState = useDesktopStore((s) => s.desktopState);
    const dispatch = useDesktopStore((s) => s.dispatch);
    const openWindow = useDesktopStore((s) => s.openWindow);
    const closeStartMenu = useDesktopStore((s) => s.closeStartMenu);
    const queryClient = useQueryClient();
    const [byName, setByName] = useState(false);
    const shortcuts = PROGRAMS.filter(
        (program) => program.shortcut && canOpen(program.access),
    );
    if (byName)
        shortcuts.sort((a, b) =>
            (a.shortcut ?? '').localeCompare(b.shortcut ?? ''),
        );
    const desktopMenu: MenuEntry[] = [
        {
            id: 'arrange',
            label: 'Arrange Icons By',
            items: [
                {
                    id: 'name',
                    label: 'Name',
                    checked: byName,
                    onSelect: () => setByName(true),
                },
            ],
        },
        {
            id: 'refresh',
            label: 'Refresh',
            onSelect: () => void queryClient.invalidateQueries(),
        },
        { type: 'separator', id: 'properties-separator' },
        {
            id: 'properties',
            label: 'Properties',
            onSelect: () => openWindow('display'),
        },
    ];

    return (
        <Desktop
            state={desktopState}
            onAction={dispatch}
            programs={desktopPrograms}
            renderIcon={renderIcon}
            wallpaper={<DesktopWallpaper />}
            desktopMenu={desktopMenu}
            icons={shortcuts.map((program) => (
                <DesktopIcon
                    key={program.id}
                    icon={<AppIcon icon={program.icon} size={48} />}
                    label={program.shortcut}
                    onClick={() => openWindow(program.id)}
                />
            ))}
            startMenu={
                <StartMenu onLaunch={openWindow} onClose={closeStartMenu} />
            }
            tray={
                <DesktopTray>
                    <NotificationTray />
                </DesktopTray>
            }
        >
            <NotificationBanners />
            <NotificationCenter />
        </Desktop>
    );
}

export default function DesktopExperience() {
    const userId = usePlayerSession((s) => s.userId);
    const hydrated = usePlayerSession((s) => s.hydrated);
    const visiting = usePlayerSession((s) => s.visiting);
    const worldId = useWorldChoice((s) => s.id);
    const checkingWorld = useForgetStoppedWorld();
    const { account, isLoading } = usePlayerAccount();
    const playerId = account?.playerId as string | null | undefined;

    const screen = () => {
        if (!hydrated || checkingWorld) return <DesktopWallpaper />;
        if (!worldId || (!userId && !visiting)) return <WelcomeScreen />;
        if (isLoading) return <DesktopWallpaper />;
        if (userId && !playerId) return <CreateCharacterScreen />;
        return (
            <section key={worldId} className="fixed inset-0">
                <DesktopProvider>
                    <DesktopWindowLayer />
                    <WorldRealtimeBridge key={playerId ?? 'visitor'} />
                </DesktopProvider>
            </section>
        );
    };

    return (
        <>
            <ClerkPlayerBridge />
            {screen()}
        </>
    );
}
