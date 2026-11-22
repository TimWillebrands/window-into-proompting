import { useId } from 'react';
import { fullDate, relativeTime } from '../../../apps/board/board-lib';
import type { DesktopNotification } from '../../../lib/notification-center';

interface NotificationCardProps {
    notification: DesktopNotification;
    now: number;
    onOpen: () => void;
    onDismiss?: () => void;
    openLabel?: string;
    variant?: 'card' | 'banner';
}

export default function NotificationCard({
    notification: n,
    now,
    onOpen,
    onDismiss,
    openLabel,
    variant = 'card',
}: NotificationCardProps) {
    const titleId = useId();
    const time = now - n.at < 60_000 ? 'now' : relativeTime(n.at, now);

    return (
        <article
            aria-labelledby={titleId}
            className="nc-card"
            data-variant={variant}
            data-unread={n.unread || undefined}
            onKeyDown={(e) => {
                if (
                    onDismiss &&
                    (e.key === 'Delete' || e.key === 'Backspace')
                ) {
                    e.preventDefault();
                    onDismiss();
                }
            }}
        >
            <button
                type="button"
                className="nc-card-hit"
                aria-label={openLabel ?? `Open: ${n.title}`}
                onClick={onOpen}
            />
            <header className="nc-card-head">
                <img
                    src={n.source.icon}
                    alt=""
                    aria-hidden
                    className="nc-app-icon"
                />
                <span className="nc-app-name">{n.source.name}</span>
                <time
                    className="nc-time"
                    dateTime={new Date(n.at).toISOString()}
                    title={fullDate(n.at)}
                >
                    {time}
                </time>
            </header>
            <h3 id={titleId} className="nc-title">
                {n.unread && (
                    <>
                        <span className="nc-dot" aria-hidden />
                        <span className="sr-only">New: </span>
                    </>
                )}
                {n.title}
            </h3>
            <p className="nc-body">{n.body}</p>
            {onDismiss && (
                <button
                    type="button"
                    className="nc-close"
                    aria-label={`Dismiss: ${n.title}`}
                    onClick={onDismiss}
                >
                    <span aria-hidden>×</span>
                </button>
            )}
        </article>
    );
}
