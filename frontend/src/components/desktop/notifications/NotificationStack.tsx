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
            <div className="nc-stack-head">
                <img
                    src={group.source.icon}
                    alt=""
                    aria-hidden
                    className="nc-app-icon"
                />
                <h2 className="nc-stack-name">{group.source.name}</h2>
                <button
                    type="button"
                    className="nc-link"
                    aria-label={`Clear ${group.source.name} notifications`}
                    onClick={() => onDismiss(group.items)}
                >
                    Clear
                </button>
                {many && (
                    <button
                        type="button"
                        className="nc-chevron"
                        data-up={expanded || undefined}
                        aria-expanded={expanded}
                        aria-controls={listId}
                        aria-label={
                            expanded
                                ? `Show less from ${group.source.name}`
                                : `Show all ${group.items.length} from ${group.source.name}`
                        }
                        onClick={() => setExpanded((e) => !e)}
                    >
                        <svg
                            aria-hidden="true"
                            viewBox="0 0 10 10"
                            width="9"
                            height="9"
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="1.6"
                        >
                            <path d="M1.5 1.5 5 5l3.5-3.5M1.5 5.5 5 9l3.5-3.5" />
                        </svg>
                    </button>
                )}
            </div>
            <ul id={listId} className="nc-stack-list nc-stack-body">
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
