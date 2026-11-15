import { useDesktopContext } from '../../lib/desktop-context';
import { useNotifications } from '../../lib/notifications';
import { useOpenNotification, usePlayerId, WORLD_ID } from '../../lib/world';
import NotificationTrayIcon from './NotificationTrayIcon';
import TrayBalloon from './TrayBalloon';

export function NotificationTray() {
    const playerId = usePlayerId();
    const { unread } = useNotifications(WORLD_ID, !!playerId);
    const { openWindow } = useDesktopContext();
    if (!playerId) return null;
    return (
        <NotificationTrayIcon
            unread={unread}
            onOpen={() => openWindow('notifications')}
        />
    );
}

export function NotificationBalloons() {
    const open = useOpenNotification();
    const { openWindow } = useDesktopContext();
    return (
        <TrayBalloon
            onOpen={(balloon) => {
                const [only] = balloon.items;
                if (balloon.items.length === 1 && only) open(only);
                else openWindow('notifications');
            }}
        />
    );
}
