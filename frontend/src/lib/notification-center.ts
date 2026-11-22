import { create } from 'zustand';
import { atOf, type PlayerNotification } from './notifications';

export interface NotificationSource {
    id: string;
    name: string;
    icon: string;
}

export const SOURCES = {
    messenger: {
        id: 'messenger',
        name: 'Messenger',
        icon: '/img/icons/messenger.ico',
    },
    board: { id: 'board', name: 'Partyboard', icon: '/img/icons/board.ico' },
    calendar: {
        id: 'calendar',
        name: 'Calendar',
        icon: '/img/icons/calendar.ico',
    },
    wardrobe: {
        id: 'wardrobe',
        name: 'Wardrobe',
        icon: '/img/icons/wardrobe.ico',
    },
    system: { id: 'system', name: 'Partytown', icon: '/img/icons/world.ico' },
} satisfies Record<string, NotificationSource>;

export type SourceId = keyof typeof SOURCES;

export interface DesktopNotification {
    id: string;
    source: NotificationSource;
    title: string;
    body: string;
    at: number;
    unread: boolean;
    world?: PlayerNotification;
    onOpen?: () => void;
}

export function sourceFor(n: PlayerNotification): NotificationSource {
    if (n.kind === 'gathering') return SOURCES.calendar;
    if (n.app === 'messenger') return SOURCES.messenger;
    if (n.app === 'board') return SOURCES.board;
    if (n.app === 'wardrobe') return SOURCES.wardrobe;
    return SOURCES.system;
}

export function fromWorld(n: PlayerNotification): DesktopNotification {
    return {
        id: n.id,
        source: sourceFor(n),
        title: n.title,
        body: n.body,
        at: atOf(n),
        unread: !!n.unread,
        world: n,
    };
}

export interface NotificationGroup {
    source: NotificationSource;
    items: DesktopNotification[];
    unread: number;
}

export function groupBySource(
    items: readonly DesktopNotification[],
): NotificationGroup[] {
    const groups = new Map<string, NotificationGroup>();
    for (const n of [...items].sort((a, b) => b.at - a.at)) {
        const g = groups.get(n.source.id) ?? {
            source: n.source,
            items: [],
            unread: 0,
        };
        g.items.push(n);
        if (n.unread) g.unread++;
        groups.set(n.source.id, g);
    }
    return [...groups.values()];
}

const SUMMARY_LINES = 3;

export function summaryFor(
    arrived: readonly DesktopNotification[],
    away: boolean,
): DesktopNotification | null {
    if (arrived.length === 0) return null;
    const newest = [...arrived].sort((a, b) => b.at - a.at);
    if (newest.length === 1 && !away) return newest[0];
    const lines = newest.slice(0, SUMMARY_LINES).map((n) => n.title);
    const more = newest.length - SUMMARY_LINES;
    if (more > 0) lines.push(`and ${more} more`);
    return {
        id: `summary:${newest.map((n) => n.id).join('|')}`,
        source: SOURCES.system,
        title: away
            ? 'While you were away'
            : `${newest.length} new notifications`,
        body: lines.join('\n'),
        at: newest[0].at,
        unread: true,
    };
}

export function announcement(n: DesktopNotification) {
    return `${n.source.name}: ${n.title}. ${n.body}`;
}

export interface NotifyInput {
    title: string;
    body: string;
    source?: SourceId;
    onOpen?: () => void;
}

const MAX_BANNERS = 4;

interface CenterStore {
    open: boolean;
    banners: DesktopNotification[];
    local: DesktopNotification[];
    announce: string;
    setOpen: (open: boolean) => void;
    toggle: () => void;
    show: (n: DesktopNotification) => void;
    retire: (id: string) => void;
    notify: (input: NotifyInput) => string;
    dismissLocal: (ids: string[] | 'all') => void;
    readLocal: () => void;
    reset: () => void;
}

export const useNotificationCenter = create<CenterStore>((set, get) => ({
    open: false,
    banners: [],
    local: [],
    announce: '',
    setOpen: (open) => set((s) => ({ open, banners: open ? [] : s.banners })),
    toggle: () => get().setOpen(!get().open),
    show: (n) =>
        set((s) => {
            const announce = announcement(n);
            if (s.open) return { announce };
            return {
                announce,
                banners: [n, ...s.banners.filter((b) => b.id !== n.id)].slice(
                    0,
                    MAX_BANNERS,
                ),
            };
        }),
    retire: (id) =>
        set((s) => ({ banners: s.banners.filter((b) => b.id !== id) })),
    notify: (input) => {
        const n: DesktopNotification = {
            id: `local:${crypto.randomUUID()}`,
            source: SOURCES[input.source ?? 'system'],
            title: input.title,
            body: input.body,
            at: Date.now(),
            unread: true,
            onOpen: input.onOpen,
        };
        set((s) => ({ local: [n, ...s.local] }));
        get().show(n);
        return n.id;
    },
    dismissLocal: (ids) =>
        set((s) => ({
            local:
                ids === 'all' ? [] : s.local.filter((n) => !ids.includes(n.id)),
            banners: s.banners.filter(
                (b) => ids !== 'all' && !ids.includes(b.id),
            ),
        })),
    readLocal: () =>
        set((s) => ({ local: s.local.map((n) => ({ ...n, unread: false })) })),
    reset: () => set({ open: false, banners: [], local: [], announce: '' }),
}));

export const notify = (input: NotifyInput) =>
    useNotificationCenter.getState().notify(input);
