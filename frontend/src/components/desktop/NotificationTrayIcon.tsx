import { useTrayBalloons } from '../../lib/notifications';

interface NotificationTrayIconProps {
    unread: number;
    onOpen: () => void;
}

export default function NotificationTrayIcon({
    unread,
    onOpen,
}: NotificationTrayIconProps) {
    const setAnchor = useTrayBalloons((s) => s.setAnchor);
    const label =
        unread === 0
            ? 'Notifications: nothing new'
            : `Notifications: ${unread} new`;

    return (
        <button
            ref={setAnchor}
            type="button"
            aria-label={label}
            title={label}
            onClick={onOpen}
            className="relative h-5 w-5 flex items-center justify-center rounded hover:bg-white/20 text-[13px] leading-none cursor-default bg-transparent border-0 text-white"
        >
            <span aria-hidden style={{ opacity: unread === 0 ? 0.75 : 1 }}>
                {unread === 0 ? '📭' : '✉️'}
            </span>
            {unread > 0 && (
                <span aria-hidden className="xp-tray-badge">
                    {unread > 99 ? '99+' : unread}
                </span>
            )}
        </button>
    );
}
