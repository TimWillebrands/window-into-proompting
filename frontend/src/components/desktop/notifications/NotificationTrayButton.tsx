import { CENTER_ID } from './NotificationCenter';

interface NotificationTrayButtonProps {
    unread: number;
    open: boolean;
    onToggle: () => void;
}

export default function NotificationTrayButton({
    unread,
    open,
    onToggle,
}: NotificationTrayButtonProps) {
    const label =
        unread === 0 ? 'Notifications' : `Notifications, ${unread} new`;
    return (
        <button
            type="button"
            data-nc-toggle
            aria-label={label}
            aria-expanded={open}
            aria-controls={CENTER_ID}
            title={label}
            onClick={onToggle}
            className="nc-tray-button"
            data-unread={unread > 0 || undefined}
        >
            <svg aria-hidden="true" fill="currentColor" viewBox="0 0 16 16" width="14" height="14">
                <rect x="2" y="2.5" width="12" height="3" rx="1" />
                <rect x="2" y="6.5" width="12" height="3" rx="1" opacity=".8" />
                <rect
                    x="2"
                    y="10.5"
                    width="12"
                    height="3"
                    rx="1"
                    opacity=".6"
                />
            </svg>
            {unread > 0 && (
                <span aria-hidden className="nc-tray-badge">
                    {unread > 99 ? '99+' : unread}
                </span>
            )}
        </button>
    );
}
