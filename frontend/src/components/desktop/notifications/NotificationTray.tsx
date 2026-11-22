import { useNotificationCenter } from '../../../lib/notification-center';
import { useCenterItems, usePlayerId } from '../../../lib/world';
import NotificationTrayButton from './NotificationTrayButton';

export default function NotificationTray() {
    const playerId = usePlayerId();
    const open = useNotificationCenter((s) => s.open);
    const toggle = useNotificationCenter((s) => s.toggle);
    const unread = useCenterItems().filter((n) => n.unread).length;
    if (!playerId) return null;
    return (
        <NotificationTrayButton unread={unread} open={open} onToggle={toggle} />
    );
}
