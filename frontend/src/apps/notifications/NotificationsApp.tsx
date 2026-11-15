import { useEffect, useRef, useState } from 'react';
import {
    atOf,
    iconFor,
    type PlayerNotification,
    useMarkNotificationsRead,
    useNotifications,
} from '../../lib/notifications';
import { useOpenNotification, usePlayerId, WORLD_ID } from '../../lib/world';
import { fullDate, relativeTime, useNow } from '../board/board-lib';

const COLUMNS = 'grid grid-cols-[24px_112px_minmax(0,1fr)_72px]';

const OPEN_LABEL: Record<string, string> = {
    messenger: 'Open conversation',
    board: 'Open on Partyboard',
    'open-party': 'Go to the room',
    places: 'Open Places',
    wardrobe: 'Open Wardrobe',
};

interface NotificationListProps {
    items: PlayerNotification[];
    fresh: ReadonlySet<string>;
    now: number;
    onOpen: (n: PlayerNotification) => void;
}

export function NotificationList({
    items,
    fresh,
    now,
    onOpen,
}: NotificationListProps) {
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const rows = useRef<HTMLUListElement>(null);
    const selected = items.find((n) => n.id === selectedId) ?? items[0] ?? null;
    const isNew = (n: PlayerNotification) => !!n.unread || fresh.has(n.id);
    const newCount = items.filter(isNew).length;

    const move = (by: number) => {
        if (items.length === 0) return;
        const at = selected ? items.indexOf(selected) : -1;
        const next = items[Math.max(0, Math.min(items.length - 1, at + by))];
        setSelectedId(next.id);
        rows.current
            ?.querySelector<HTMLButtonElement>(
                `[data-id="${CSS.escape(next.id)}"]`,
            )
            ?.focus();
    };

    return (
        <div className="app-surface flex h-full flex-col bg-[#ece9d8]">
            <div className="flex items-center gap-1 border-b border-[#aca899] px-2 py-1">
                <button
                    type="button"
                    disabled={!selected}
                    onClick={() => selected && onOpen(selected)}
                >
                    {selected ? (OPEN_LABEL[selected.app] ?? 'Open') : 'Open'}
                </button>
            </div>

            <div className="m-2 mb-0 min-h-0 flex-1 overflow-auto border border-[#7f9db9] bg-white">
                {items.length === 0 ? (
                    <p className="p-4 text-center text-[#555]">
                        You're all caught up. Messages, replies, Respect! and
                        presents show up here.
                    </p>
                ) : (
                    <>
                        <div
                            aria-hidden
                            className={`${COLUMNS} sticky top-0 bg-[#ece9d8] [&>span]:border-r [&>span]:border-b [&>span]:border-[#aca899] [&>span]:px-1.5 [&>span]:py-0.5`}
                        >
                            <span />
                            <span>From</span>
                            <span>Subject</span>
                            <span>Received</span>
                        </div>
                        <ul
                            ref={rows}
                            aria-label="Notifications"
                            className="m-0 list-none p-0"
                        >
                            {items.map((n) => {
                                const on = selected?.id === n.id;
                                return (
                                    <li key={n.id}>
                                        <button
                                            type="button"
                                            data-id={n.id}
                                            aria-current={on}
                                            onClick={() => setSelectedId(n.id)}
                                            onFocus={() => setSelectedId(n.id)}
                                            onDoubleClick={() => onOpen(n)}
                                            onKeyDown={(e) => {
                                                if (
                                                    e.key === 'ArrowDown' ||
                                                    e.key === 'j'
                                                )
                                                    move(1);
                                                else if (
                                                    e.key === 'ArrowUp' ||
                                                    e.key === 'k'
                                                )
                                                    move(-1);
                                                else if (e.key === 'Enter')
                                                    onOpen(n);
                                                else return;
                                                e.preventDefault();
                                            }}
                                            className={`xp-bare ${COLUMNS} w-full cursor-default border-0 bg-transparent p-0 text-left text-inherit outline-none focus-visible:outline-1 focus-visible:outline-dotted focus-visible:outline-black [&>span]:truncate [&>span]:px-1.5 [&>span]:py-0.5 ${on ? 'bg-[#316ac5]! text-white' : 'hover:bg-[#e8eef9]'} ${isNew(n) ? '[&>span]:font-bold' : ''}`}
                                        >
                                            <span
                                                aria-hidden
                                                className="text-center"
                                            >
                                                {iconFor(n.kind)}
                                            </span>
                                            <span>
                                                {n.fromName ?? 'Partytown'}
                                            </span>
                                            <span>{n.title}</span>
                                            <span title={fullDate(atOf(n))}>
                                                {relativeTime(atOf(n), now)}
                                            </span>
                                        </button>
                                    </li>
                                );
                            })}
                        </ul>
                    </>
                )}
            </div>

            {selected && (
                <div className="m-2 mb-0 border border-[#7f9db9] bg-white p-2">
                    <div className="mb-1 flex gap-3 border-b border-[#ece9d8] pb-1 text-[#444]">
                        <span>
                            <span className="text-[#888]">From:</span>{' '}
                            {selected.fromName ?? 'Partytown'}
                        </span>
                        <span>
                            <span className="text-[#888]">Received:</span>{' '}
                            {fullDate(atOf(selected))}
                        </span>
                    </div>
                    <p className="font-bold">
                        <span aria-hidden>{iconFor(selected.kind)} </span>
                        {selected.title}
                    </p>
                    <p className="mt-0.5 whitespace-pre-line break-words">
                        {selected.body}
                    </p>
                </div>
            )}

            <div className="mt-2 flex border-t border-[#aca899] text-[#333] [&>span]:border-r [&>span]:border-[#aca899] [&>span]:px-2 [&>span]:py-0.5">
                <span>
                    {items.length} notification{items.length === 1 ? '' : 's'}
                </span>
                <span>{newCount} new</span>
                <span className="flex-1 border-r-0!">Last 3 days</span>
            </div>
        </div>
    );
}

export default function NotificationsApp() {
    const playerId = usePlayerId();
    const { feed, loaded } = useNotifications(WORLD_ID, !!playerId);
    const markRead = useRef(useMarkNotificationsRead(WORLD_ID));
    const open = useOpenNotification();
    const now = useNow();
    const [fresh, setFresh] = useState<ReadonlySet<string>>(new Set());
    const unreadIds = feed.items
        .filter((n) => n.unread)
        .map((n) => n.id)
        .join(',');

    useEffect(() => {
        if (!loaded || !unreadIds) return;
        setFresh((s) => new Set([...s, ...unreadIds.split(',')]));
        void markRead.current({ all: true });
    }, [loaded, unreadIds]);

    if (!playerId)
        return (
            <p className="app-surface p-4">
                Sign in and make a character to get notifications.
            </p>
        );

    return (
        <NotificationList
            items={feed.items}
            fresh={fresh}
            now={now}
            onOpen={(n) => open(n)}
        />
    );
}
