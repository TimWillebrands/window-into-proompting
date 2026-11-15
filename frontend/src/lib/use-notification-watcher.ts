import { useEffect, useRef } from 'react';
import { useDesktopContext } from './desktop-context';
import {
    arrivals,
    balloonFor,
    type PlayerNotification,
    tabTitle,
    targetWindowId,
    useMarkNotificationsRead,
    useNotifications,
    useTrayBalloons,
} from './notifications';

const AWAY_MS = 5 * 60_000;
const NOTIFICATIONS_WINDOW = 'notifications';

export function useNotificationWatcher(
    worldId: string,
    playerId: string | null,
) {
    const { feed, unread, loaded } = useNotifications(worldId, !!playerId);
    const markRead = useMarkNotificationsRead(worldId);
    const show = useTrayBalloons((s) => s.show);
    const clear = useTrayBalloons((s) => s.clear);
    const { desktopState } = useDesktopContext();

    const known = useRef<Set<string> | null>(null);
    const pending = useRef<PlayerNotification[]>([]);
    const lastInput = useRef(Date.now());
    const latest = useRef({ feed, desktopState, markRead });
    latest.current = { feed, desktopState, markRead };

    useEffect(() => {
        const flush = () => {
            const unreadNow = new Set(
                latest.current.feed.items
                    .filter((n) => n.unread)
                    .map((n) => n.id),
            );
            const back = pending.current.filter((n) => unreadNow.has(n.id));
            pending.current = [];
            const balloon = balloonFor(back, true);
            if (balloon) show(balloon);
        };
        const active = () => {
            const wasAway = Date.now() - lastInput.current > AWAY_MS;
            lastInput.current = Date.now();
            if (wasAway) flush();
        };
        const visibility = () => {
            if (document.hidden) return;
            lastInput.current = Date.now();
            flush();
        };
        const events = ['pointerdown', 'keydown', 'wheel'] as const;
        for (const e of events)
            window.addEventListener(e, active, { passive: true });
        document.addEventListener('visibilitychange', visibility);
        return () => {
            for (const e of events) window.removeEventListener(e, active);
            document.removeEventListener('visibilitychange', visibility);
            clear();
        };
    }, [show, clear]);

    useEffect(() => {
        if (!loaded) return;
        const items = feed.items;
        const away =
            document.hidden || Date.now() - lastInput.current > AWAY_MS;

        if (known.current === null) {
            known.current = new Set(items.map((n) => n.id));
            const waiting = items.filter((n) => n.unread);
            if (document.hidden) pending.current = waiting;
            else {
                const balloon = balloonFor(waiting, true);
                if (balloon) show(balloon);
            }
            return;
        }

        const arrived = arrivals(known.current, items);
        for (const n of items) known.current.add(n.id);
        if (arrived.length === 0) return;

        const { desktopState: desk, markRead: mark } = latest.current;
        const focused = desk.focusedId;
        const looking = (n: PlayerNotification) =>
            !document.hidden &&
            !!focused &&
            !desk.windows[focused]?.minimized &&
            (focused === NOTIFICATIONS_WINDOW || focused === targetWindowId(n));
        const seen = arrived.filter(looking);
        if (seen.length > 0) void mark({ ids: seen.map((n) => n.id) });
        const rest = arrived.filter((n) => !looking(n));
        if (rest.length === 0) return;
        if (away) {
            pending.current.push(...rest);
            return;
        }
        const balloon = balloonFor(rest, false);
        if (balloon) show(balloon);
    }, [feed, loaded, show]);

    useEffect(() => {
        const focused = desktopState.focusedId;
        if (!focused || desktopState.windows[focused]?.minimized) return;
        const opened = feed.items.filter(
            (n) => n.unread && targetWindowId(n) === focused,
        );
        if (opened.length > 0)
            void latest.current.markRead({ ids: opened.map((n) => n.id) });
    }, [desktopState.focusedId, desktopState.windows, feed.items]);

    useEffect(() => {
        document.title = tabTitle(document.title, unread);
    }, [unread]);

    useEffect(
        () => () => {
            document.title = tabTitle(document.title, 0);
        },
        [],
    );
}
