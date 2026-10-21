import { Fragment, type ReactNode } from 'react';
import { create } from 'zustand';

import type { LifeSummary } from '../../api/model/lifeSummary';
import type { SelfSummary } from '../../api/model/selfSummary';

export type BuddyStatus = 'online' | 'away' | 'busy' | 'offline';

/** Busy while acting; Away when their social battery is nearly empty (ADR 0021). */
export const statusOf = (
    life: LifeSummary | undefined,
    self?: SelfSummary,
): BuddyStatus =>
    !life
        ? 'offline'
        : life.busy
          ? 'busy'
          : self && Number(self.battery) < 0.25
            ? 'away'
            : 'online';

const STATUS_COLORS: Record<BuddyStatus, [string, string]> = {
    online: ['#7ed957', '#2f8f1c'],
    away: ['#ffd75e', '#c79a00'],
    busy: ['#ff7b6b', '#b8241a'],
    offline: ['#c9c9c9', '#7c7c7c'],
};

export const STATUS_LABEL: Record<BuddyStatus, string> = {
    online: 'Online',
    away: 'Away',
    busy: 'Busy',
    offline: 'Offline',
};

/** Classic MSN buddy glyph tinted by presence. */
export function BuddyIcon({
    status,
    size = 16,
}: {
    status: BuddyStatus;
    size?: number;
}) {
    const [light, dark] = STATUS_COLORS[status];
    return (
        <svg
            width={size}
            height={size}
            viewBox="0 0 16 16"
            aria-hidden="true"
            style={{ flexShrink: 0 }}
        >
            <circle
                cx="8"
                cy="4.6"
                r="3.4"
                fill={light}
                stroke={dark}
                strokeWidth="0.7"
            />
            <path
                d="M2.2 15.2c0-3.6 2.6-6 5.8-6s5.8 2.4 5.8 6z"
                fill={light}
                stroke={dark}
                strokeWidth="0.7"
            />
            <ellipse
                cx="6.9"
                cy="3.5"
                rx="1.3"
                ry="0.9"
                fill="#fff"
                opacity="0.8"
            />
            <ellipse
                cx="6"
                cy="11.4"
                rx="2"
                ry="0.9"
                fill="#fff"
                opacity="0.55"
            />
        </svg>
    );
}

export const personaAvatar = (personaId: string, px: number) =>
    `https://robohash.org/${personaId}.png?size=${px}x${px}`;

export const playerAvatar = (name: string, px: number) =>
    `https://robohash.org/${encodeURIComponent(name)}.png?size=${px}x${px}&set=set5`;

/** Display picture with the glossy MSN frame. */
export function DisplayPicture({ src, size }: { src: string; size: number }) {
    return (
        <div
            style={{
                width: size + 8,
                height: size + 8,
                padding: 3,
                borderRadius: 6,
                background: 'linear-gradient(180deg, #ffffff 0%, #c9dbf5 100%)',
                border: '1px solid #8eaedc',
                boxShadow:
                    'inset 0 1px 0 #fff, 0 1px 2px rgba(31, 55, 148, 0.25)',
                flexShrink: 0,
            }}
        >
            <img
                src={src}
                alt=""
                width={size}
                height={size}
                style={{
                    width: size,
                    height: size,
                    borderRadius: 3,
                    background: '#fff',
                    border: '1px solid #b5c8e8',
                    display: 'block',
                }}
            />
        </div>
    );
}

const EMOTICONS: Array<[RegExp, string]> = [
    [/(^|\s):-?\)(?=\s|$)/g, '$1🙂'],
    [/(^|\s):-?\((?=\s|$)/g, '$1🙁'],
    [/(^|\s):-?D(?=\s|$)/g, '$1😄'],
    [/(^|\s);-?\)(?=\s|$)/g, '$1😉'],
    [/(^|\s):-?P(?=\s|$)/gi, '$1😛'],
    [/(^|\s)<3(?=\s|$)/g, '$1❤️'],
];

const emoticonize = (text: string) =>
    EMOTICONS.reduce((acc, [re, to]) => acc.replace(re, to), text);

/** Plain text with *emotes* in italics and text emoticons swapped for glyphs. */
export function MsnText({ text }: { text: string }) {
    const parts = emoticonize(text).split(/(\*[^*\n]+\*)/g);
    return (
        <>
            {parts.map((part, i) => {
                const key = `${i}-${part.length}`;
                if (/^\*[^*\n]+\*$/.test(part)) {
                    return (
                        <em key={key} style={{ color: '#5a5a5a' }}>
                            {part.slice(1, -1)}
                        </em>
                    );
                }
                return <Fragment key={key}>{part}</Fragment>;
            })}
        </>
    );
}

export const clip = (s: string | null | undefined, n: number) =>
    !s ? '' : s.length > n ? `${s.slice(0, n)}…` : s;

export function toMillis(v: number | string | null | undefined): number {
    if (v == null) return 0;
    const n = typeof v === 'string' ? Number(v) : v;
    return Number.isFinite(n) ? n : new Date(v).getTime() || 0;
}

export function shortTime(ms: number): string {
    if (!ms) return '';
    const d = new Date(ms);
    const sameDay = d.toDateString() === new Date().toDateString();
    return sameDay
        ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
        : d.toLocaleDateString([], { day: 'numeric', month: 'short' });
}

/** Group header with the MSN collapse triangle. */
export function GroupHeader({
    label,
    count,
    open,
    onToggle,
    extra,
}: {
    label: string;
    count: number;
    open: boolean;
    onToggle: () => void;
    extra?: ReactNode;
}) {
    return (
        <button
            type="button"
            className="xp-bare flex w-full items-center gap-1 px-2 py-[3px] text-left"
            onClick={onToggle}
            style={{
                background: 'transparent',
                border: 'none',
                color: '#1d3f73',
                fontWeight: 700,
                fontSize: 11,
                cursor: 'pointer',
            }}
        >
            <span
                style={{
                    display: 'inline-block',
                    width: 9,
                    fontSize: 8,
                    color: '#4a6ea9',
                    transform: open ? 'rotate(90deg)' : 'none',
                    transition: 'transform 120ms',
                }}
            >
                ▶
            </span>
            <span>
                {label} ({count})
            </span>
            {extra}
        </button>
    );
}

interface SeenStore {
    seen: Record<string, number>;
    markSeen: (chatGroupId: string, at: number) => void;
}

const SEEN_KEY = 'messenger-seen';

const loadSeen = (): Record<string, number> => {
    try {
        return JSON.parse(localStorage.getItem(SEEN_KEY) ?? '{}');
    } catch {
        return {};
    }
};

/** Per-DM "read up to" timestamps, kept in localStorage. */
export const useMessengerSeen = create<SeenStore>((set, get) => ({
    seen: typeof localStorage === 'undefined' ? {} : loadSeen(),
    markSeen: (chatGroupId, at) => {
        if ((get().seen[chatGroupId] ?? 0) >= at) return;
        const seen = { ...get().seen, [chatGroupId]: at };
        try {
            localStorage.setItem(SEEN_KEY, JSON.stringify(seen));
        } catch {
            /* storage full or blocked */
        }
        set({ seen });
    },
}));
