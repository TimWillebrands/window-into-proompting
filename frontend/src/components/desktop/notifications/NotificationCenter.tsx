import { useEffect, useRef } from 'react';
import { useNow } from '../../../apps/board/board-lib';
import {
    type DesktopNotification,
    groupBySource,
    useNotificationCenter,
} from '../../../lib/notification-center';
import { useCenterItems, useNotificationActions } from '../../../lib/world';
import NotificationStack from './NotificationStack';

export const CENTER_ID = 'notification-center';
export const TOGGLE_SELECTOR = '[data-nc-toggle]';

interface NotificationCenterPanelProps {
    open: boolean;
    items: DesktopNotification[];
    now: number;
    onClose: () => void;
    onOpen: (n: DesktopNotification) => void;
    onDismiss: (ns: DesktopNotification[]) => void;
    onClearAll: () => void;
}

export function NotificationCenterPanel({
    open,
    items,
    now,
    onClose,
    onOpen,
    onDismiss,
    onClearAll,
}: NotificationCenterPanelProps) {
    const heading = useRef<HTMLHeadingElement>(null);
    const groups = groupBySource(items);
    const date = new Date(now);

    useEffect(() => {
        if (open) heading.current?.focus();
    }, [open]);

    return (
        <aside
            id={CENTER_ID}
            aria-label="Notification Center"
            className="nc-center"
            data-open={open || undefined}
            inert={!open}
            onKeyDown={(e) => {
                if (e.key !== 'Escape') return;
                e.stopPropagation();
                onClose();
            }}
        >
            <div className="nc-clock" aria-hidden>
                <span className="nc-clock-time">
                    {date.toLocaleTimeString([], {
                        hour: '2-digit',
                        minute: '2-digit',
                    })}
                </span>
                <span className="nc-clock-date">
                    {date.toLocaleDateString([], {
                        weekday: 'long',
                        day: 'numeric',
                        month: 'long',
                    })}
                </span>
            </div>
            <div className="nc-center-head">
                <h2 ref={heading} tabIndex={-1} className="nc-center-title">
                    Notifications
                </h2>
                {items.length > 0 && (
                    <button
                        type="button"
                        className="nc-pill"
                        onClick={onClearAll}
                    >
                        Clear all
                    </button>
                )}
                <button
                    type="button"
                    className="nc-pill"
                    aria-label="Close Notification Center"
                    onClick={onClose}
                >
                    <span aria-hidden>»</span>
                </button>
            </div>
            <div className="nc-center-scroll">
                {groups.length === 0 ? (
                    <p className="nc-empty">
                        Nothing new. Messages, replies, Respect! and presents
                        land here.
                    </p>
                ) : (
                    groups.map((g) => (
                        <NotificationStack
                            key={g.source.id}
                            group={g}
                            now={now}
                            onOpen={onOpen}
                            onDismiss={onDismiss}
                        />
                    ))
                )}
            </div>
        </aside>
    );
}

export default function NotificationCenter() {
    const open = useNotificationCenter((s) => s.open);
    const setOpen = useNotificationCenter((s) => s.setOpen);
    const items = useCenterItems();
    const actions = useNotificationActions();
    const now = useNow(15_000);
    const unread = items.some((n) => n.unread);
    const readAll = useRef(actions.readAll);
    readAll.current = actions.readAll;

    useEffect(() => {
        if (open && unread) readAll.current();
    }, [open, unread]);

    const close = () => {
        setOpen(false);
        document.querySelector<HTMLElement>(TOGGLE_SELECTOR)?.focus();
    };

    return (
        <NotificationCenterPanel
            open={open}
            items={items}
            now={now}
            onClose={close}
            onOpen={(n) => {
                setOpen(false);
                actions.open(n);
            }}
            onDismiss={actions.dismiss}
            onClearAll={actions.clearAll}
        />
    );
}
