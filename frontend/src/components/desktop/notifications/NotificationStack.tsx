import { type CSSProperties, useId, useState } from 'react';
import type {
    DesktopNotification,
    NotificationGroup,
} from '../../../lib/notification-center';
import NotificationCard from './NotificationCard';

interface NotificationStackProps {
    group: NotificationGroup;
    now: number;
    onOpen: (n: DesktopNotification) => void;
    onDismiss: (ns: DesktopNotification[]) => void;
    initiallyExpanded?: boolean;
}

const PEEK = 2;

export default function NotificationStack({
    group,
    now,
    onOpen,
    onDismiss,
    initiallyExpanded = false,
}: NotificationStackProps) {
    const [expanded, setExpanded] = useState(initiallyExpanded);
    const listId = useId();
    const [top, ...rest] = group.items;
    const many = rest.length > 0;
    const open = expanded || !many;

    return (
        <section
            className="nc-stack"
            data-expanded={open || undefined}
            aria-label={`${group.source.name}, ${group.items.length} notification${group.items.length === 1 ? '' : 's'}${group.unread ? `, ${group.unread} new` : ''}`}
        >
            {many && (
                <div
                    className="nc-stack-head"
                    data-shown={expanded || undefined}
                >
                    <h2 className="nc-stack-name">{group.source.name}</h2>
                    <button
                        type="button"
                        className="nc-pill"
                        aria-expanded={expanded}
                        aria-controls={listId}
                        onClick={() => setExpanded((e) => !e)}
                    >
                        {expanded ? 'Show less' : `Show ${rest.length} more`}
                    </button>
                    <button
                        type="button"
                        className="nc-pill"
                        aria-label={`Clear ${group.source.name} notifications`}
                        onClick={() => onDismiss(group.items)}
                    >
                        Clear
                    </button>
                </div>
            )}
            <ul id={listId} className="nc-stack-list">
                <li className="nc-stack-item">
                    <NotificationCard
                        notification={top}
                        now={now}
                        openLabel={
                            open
                                ? undefined
                                : `${top.title}. ${rest.length} more from ${group.source.name}: show all`
                        }
                        onOpen={() => (open ? onOpen(top) : setExpanded(true))}
                        onDismiss={() => onDismiss(open ? [top] : group.items)}
                    />
                    {!open && (
                        <>
                            {rest.slice(0, PEEK).map((n, i) => (
                                <span
                                    key={n.id}
                                    aria-hidden
                                    className="nc-sliver"
                                    style={
                                        {
                                            '--depth': i + 1,
                                        } as CSSProperties
                                    }
                                />
                            ))}
                            <span aria-hidden className="nc-count">
                                +{rest.length}
                            </span>
                        </>
                    )}
                </li>
                {open &&
                    rest.map((n) => (
                        <li key={n.id} className="nc-stack-item nc-unfold">
                            <NotificationCard
                                notification={n}
                                now={now}
                                onOpen={() => onOpen(n)}
                                onDismiss={() => onDismiss([n])}
                            />
                        </li>
                    ))}
            </ul>
        </section>
    );
}
