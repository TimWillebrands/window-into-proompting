import { useEffect, useRef } from 'react';
import { postWorldsPartyIdPlacesIdle } from '#api/party-zone';
import { usePlayerId, WORLD_ID } from '../../lib/world';

const IDLE_AFTER_MS = 5 * 60_000;
const HEARTBEAT_MS = 60_000;

/** Hidden or untouched tab: the Body dozes; input heartbeats ≤1/min, ten quiet minutes and the server sends them home AFK. */
export function usePlayerIdle() {
    const playerId = usePlayerId();
    const idle = useRef(false);
    useEffect(() => {
        if (!playerId) return;
        let timer: ReturnType<typeof setTimeout> | undefined;
        let lastBeat = 0;
        const post = (next: boolean) =>
            void postWorldsPartyIdPlacesIdle(WORLD_ID, {
                idle: next,
                participantId: playerId,
            }).catch(() => {});
        const goIdle = () => {
            if (idle.current) return;
            idle.current = true;
            post(true);
        };
        const active = () => {
            const now = Date.now();
            if (idle.current || now - lastBeat >= HEARTBEAT_MS) {
                idle.current = false;
                lastBeat = now;
                post(false);
            }
            clearTimeout(timer);
            timer = setTimeout(goIdle, IDLE_AFTER_MS);
        };
        const visibility = () => (document.hidden ? goIdle() : active());
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
