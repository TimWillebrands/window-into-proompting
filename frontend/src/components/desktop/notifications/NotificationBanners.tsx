import { type CSSProperties, useEffect, useRef, useState } from 'react';
import { useNow } from '../../../apps/board/board-lib';
import { useConsentPrompt } from '../../../lib/analytics';
import {
    type DesktopNotification,
    useNotificationCenter,
} from '../../../lib/notification-center';
import { useNotificationActions } from '../../../lib/world';
import NotificationCard from './NotificationCard';

const SHOW_MS = 7_000;

interface NotificationBannersViewProps {
    banners: DesktopNotification[];
    now: number;
    raised?: boolean;
    onOpen: (n: DesktopNotification) => void;
    onRetire: (n: DesktopNotification) => void;
}

export function NotificationBannersView({
    banners,
    now,
    raised = false,
    onOpen,
    onRetire,
}: NotificationBannersViewProps) {
    const [held, setHeld] = useState(false);
    const front = banners[0];
    const retire = useRef(onRetire);
    retire.current = onRetire;

    useEffect(() => {
        if (!front || held) return;
        const t = window.setTimeout(() => retire.current(front), SHOW_MS);
        return () => window.clearTimeout(t);
    }, [front, held]);

    if (banners.length === 0) return null;

    return (
        <section
            aria-label="New notifications"
            className="nc-banners"
            data-fanned={held || undefined}
            data-raised={raised || undefined}
            onPointerEnter={() => setHeld(true)}
            onPointerLeave={() => setHeld(false)}
            onFocus={() => setHeld(true)}
            onBlur={(e) => {
                if (!e.currentTarget.contains(e.relatedTarget)) setHeld(false);
            }}
        >
            <ol className="nc-banner-list">
                {banners.map((n, i) => (
                    <li
                        key={n.id}
                        className="nc-banner"
                        style={{ '--i': i } as CSSProperties}
                        aria-hidden={!held && i > 0 ? true : undefined}
                    >
                        <NotificationCard
                            notification={n}
                            now={now}
                            variant="banner"
                            onOpen={() => onOpen(n)}
                            onDismiss={() => onRetire(n)}
                        />
                    </li>
                ))}
            </ol>
        </section>
    );
}

export default function NotificationBanners() {
    const banners = useNotificationCenter((s) => s.banners);
    const retire = useNotificationCenter((s) => s.retire);
    const announce = useNotificationCenter((s) => s.announce);
    const cookieOpen = useConsentPrompt((s) => s.open);
    const actions = useNotificationActions();
    const now = useNow(15_000);

    return (
        <>
            <div className="sr-only" aria-live="polite" aria-atomic>
                {announce}
            </div>
            <NotificationBannersView
                banners={banners}
                now={now}
                raised={cookieOpen}
                onOpen={(n) => {
                    retire(n.id);
                    actions.open(n);
                }}
                onRetire={(n) => retire(n.id)}
            />
        </>
    );
}
