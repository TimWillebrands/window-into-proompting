import { useEffect, useRef } from 'react';
import { postWorldsPartyIdPlacesIdle } from '#api/party-zone';
import { usePlayerId, WORLD_ID } from '../../lib/world';

const IDLE_AFTER_MS = 5 * 60_000;

/** The human's Body dozes off in Places when the tab is hidden or untouched for a while (spec § quasi-meta). */
export function usePlayerIdle() {
    const playerId = usePlayerId();
    const idle = useRef(false);
    useEffect(() => {
        if (!playerId) return;
        let timer: ReturnType<typeof setTimeout> | undefined;
        const report = (next: boolean) => {
            if (idle.current === next) return;
            idle.current = next;
            void postWorldsPartyIdPlacesIdle(WORLD_ID, {
                idle: next,
                participantId: playerId,
            }).catch(() => {});
        };
        const active = () => {
            report(false);
            clearTimeout(timer);
            timer = setTimeout(() => report(true), IDLE_AFTER_MS);
        };
        const visibility = () => (document.hidden ? report(true) : active());
        const events = ['pointerdown', 'keydown', 'wheel'] as const;
        for (const e of events)
            window.addEventListener(e, active, { passive: true });
        document.addEventListener('visibilitychange', visibility);
        active();
        return () => {
            clearTimeout(timer);
            for (const e of events) window.removeEventListener(e, active);
            document.removeEventListener('visibilitychange', visibility);
        };
    }, [playerId]);
}
