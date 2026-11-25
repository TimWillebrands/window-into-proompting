import {
    Presence,
    type PresenceStatus,
    Text,
    type UserPictureSize,
} from '@partytown/ui';
import { Fragment } from 'react';
import { create } from 'zustand';

import type { LifeSummary } from '../../api/model/lifeSummary';
import type { SelfSummary } from '../../api/model/selfSummary';
import Portrait from '../../components/Portrait';

export type BuddyStatus = 'online' | 'away' | 'busy' | 'afk' | 'offline';

export const statusOf = (
    life: LifeSummary | undefined,
    self?: SelfSummary,
): BuddyStatus =>
    !life
        ? 'offline'
        : life.afk
          ? 'afk'
          : life.busy
            ? 'busy'
            : self &&
                (Number(self.battery) < 0.25 ||
                    Number(self.availability) < 0.35)
              ? 'away'
              : 'online';

export const STATUS_LABEL: Record<BuddyStatus, string> = {
    online: 'Online',
    away: 'Away',
    busy: 'Busy',
    afk: 'AFK',
    offline: 'Offline',
};

const PRESENCE: Record<BuddyStatus, PresenceStatus> = {
    online: 'online',
    away: 'away',
    busy: 'busy',
    afk: 'away',
    offline: 'offline',
};

export function BuddyIcon({
    status,
    size = 'medium',
    active,
}: {
    status: BuddyStatus;
    size?: 'small' | 'medium';
    active?: boolean;
}) {
    return (
        <Presence
            shape="buddy"
            status={PRESENCE[status]}
            size={size}
            active={active}
        />
    );
}

export function DisplayPicture({
    who,
    size,
}: {
    who: string | null | undefined;
    size: UserPictureSize;
}) {
    return <Portrait who={who} size={size} />;
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

export function MsnText({ text }: { text: string }) {
    const parts = emoticonize(text).split(/(\*[^*\n]+\*)/g);
    return (
        <>
            {parts.map((part, i) => {
                const key = `${i}-${part.length}`;
                if (/^\*[^*\n]+\*$/.test(part)) {
                    return (
                        <Text key={key} as="em" italic tone="muted">
                            {part.slice(1, -1)}
                        </Text>
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

export const useMessengerSeen = create<SeenStore>((set, get) => ({
    seen: typeof localStorage === 'undefined' ? {} : loadSeen(),
    markSeen: (chatGroupId, at) => {
        if ((get().seen[chatGroupId] ?? 0) >= at) return;
        const seen = { ...get().seen, [chatGroupId]: at };
        try {
            localStorage.setItem(SEEN_KEY, JSON.stringify(seen));
        } catch {}
        set({ seen });
    },
}));
