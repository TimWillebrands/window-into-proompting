import { useEffect, useLayoutEffect, useState } from 'react';
import { useConsentPrompt } from '../../lib/analytics';
import { type Balloon, useTrayBalloons } from '../../lib/notifications';

const SHOW_MS = 10_000;
const TAIL_INSET = 11;
const TASKBAR = 30;

interface TrayBalloonViewProps {
    balloon: Balloon;
    right: number;
    bottom: number;
    onOpen: () => void;
    onClose: () => void;
}

export function TrayBalloonView({
    balloon,
    right,
    bottom,
    onOpen,
    onClose,
}: TrayBalloonViewProps) {
    return (
        <aside
            aria-live="polite"
            aria-label={balloon.title}
            className="xp-balloon"
            style={{ right, bottom }}
        >
            <div className="xp-balloon-head">
                <span aria-hidden className="xp-balloon-icon">
                    {balloon.icon}
                </span>
                <strong className="xp-balloon-title">{balloon.title}</strong>
                <button
                    type="button"
                    aria-label="Close"
                    className="xp-balloon-close"
                    onClick={onClose}
                >
                    ×
                </button>
            </div>
            <button type="button" className="xp-balloon-body" onClick={onOpen}>
                {balloon.body}
            </button>
            <span aria-hidden className="xp-balloon-tail" />
        </aside>
    );
}

function useAnchorRight(anchor: HTMLElement | null) {
    const [right, setRight] = useState(8);
    useLayoutEffect(() => {
        const measure = () => {
            if (!anchor) return setRight(8);
            const r = anchor.getBoundingClientRect();
            const centre = r.left + r.width / 2;
            setRight(Math.max(4, window.innerWidth - centre - TAIL_INSET));
        };
        measure();
        window.addEventListener('resize', measure);
        return () => window.removeEventListener('resize', measure);
    }, [anchor]);
    return right;
}

export default function TrayBalloon({
    onOpen,
}: {
    onOpen: (balloon: Balloon) => void;
}) {
    const balloon = useTrayBalloons((s) => s.queue[0]);
    const anchor = useTrayBalloons((s) => s.anchor);
    const dismiss = useTrayBalloons((s) => s.dismiss);
    const cookieOpen = useConsentPrompt((s) => s.open);
    const right = useAnchorRight(anchor);
    const [hovered, setHovered] = useState(false);

    useEffect(() => {
        if (!balloon || hovered) return;
        const t = window.setTimeout(() => dismiss(balloon.id), SHOW_MS);
        return () => window.clearTimeout(t);
    }, [balloon, hovered, dismiss]);

    if (!balloon) return null;

    return (
        <div
            onPointerEnter={() => setHovered(true)}
            onPointerLeave={() => setHovered(false)}
        >
            <TrayBalloonView
                key={balloon.id}
                balloon={balloon}
                right={right}
                bottom={TASKBAR + 10 + (cookieOpen ? 32 : 0)}
                onOpen={() => {
                    dismiss(balloon.id);
                    onOpen(balloon);
                }}
                onClose={() => dismiss(balloon.id)}
            />
        </div>
    );
}
