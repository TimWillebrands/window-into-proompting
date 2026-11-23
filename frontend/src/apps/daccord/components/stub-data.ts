import type { IconName } from '@partytown/ui';

export interface StubRoom {
    id: string;
    name: string;
    description: string;
    online: number;
    members: number;
    icon: IconName;
    category: 'featured' | 'popular' | 'recent';
}

export interface StubMember {
    id: string;
    name: string;
    handle: string;
    lastSeen: string;
    avatarSeed: string;
    online: boolean;
}

export interface StubActivity {
    id: string;
    name: string;
    action: string;
    when: string;
    avatarSeed: string;
}

export interface StubCategory {
    id: string;
    label: string;
    icon: IconName;
}

export const NAV_CATEGORIES: StubCategory[] = [
    { id: 'home', label: 'Home', icon: 'home' },
    { id: 'music', label: 'Music', icon: 'music' },
    { id: 'gaming', label: 'Gaming', icon: 'games' },
    { id: 'education', label: 'Education', icon: 'education' },
    { id: 'science', label: 'Science & Tech', icon: 'science' },
    { id: 'entertainment', label: 'Entertainment', icon: 'film' },
    { id: 'student', label: 'Student Hubs', icon: 'users' },
];

export const SERVER_RAIL: StubCategory[] = [
    { id: 'planet', label: 'All', icon: 'planet' },
    { id: 'music', label: 'Music', icon: 'music' },
    { id: 'gaming', label: 'Gaming', icon: 'games' },
    { id: 'education', label: 'Education', icon: 'education' },
    { id: 'science', label: 'Science', icon: 'science' },
    { id: 'entertainment', label: 'Entertainment', icon: 'play' },
    { id: 'student', label: 'Student Hubs', icon: 'users' },
];

export const FEATURED_ROOMS: StubRoom[] = [
    {
        id: 'stub-vr',
        name: 'Virtual Reality',
        description:
            'A community for VR and novices alike, regular and friendly chat.',
        online: 5786,
        members: 45098,
        icon: 'vr-headset',
        category: 'featured',
    },
    {
        id: 'stub-gameplay',
        name: 'Game Play',
        description: 'Always a new challenge. Great place to make new friends.',
        online: 1786,
        members: 101098,
        icon: 'games',
        category: 'featured',
    },
];

export const POPULAR_ROOMS: StubRoom[] = [
    {
        id: 'stub-3dart',
        name: '3D Art',
        description: 'A great place to discuss art.',
        online: 412,
        members: 85098,
        icon: 'palette',
        category: 'popular',
    },
    {
        id: 'stub-nft',
        name: 'NFT',
        description: 'An NFT community so that everyone can share their NFTs.',
        online: 233,
        members: 5093,
        icon: 'coin',
        category: 'popular',
    },
];

export const RECENT_ROOMS: StubRoom[] = [
    {
        id: 'stub-cinema',
        name: 'Cinema',
        description: 'Movie buffs, hot takes, weekly watch parties.',
        online: 88,
        members: 1204,
        icon: 'film',
        category: 'recent',
    },
    {
        id: 'stub-cosmos',
        name: 'Cosmos',
        description: 'Space, telescopes, and the occasional alien debate.',
        online: 142,
        members: 3304,
        icon: 'galaxy',
        category: 'recent',
    },
    {
        id: 'stub-skies',
        name: 'Open Skies',
        description: 'Sunset photos, hiking, and outdoor talk.',
        online: 76,
        members: 989,
        icon: 'mountain',
        category: 'recent',
    },
];

export const ALL_STUB_ROOMS: StubRoom[] = [
    ...FEATURED_ROOMS,
    ...POPULAR_ROOMS,
    ...RECENT_ROOMS,
];

const REAL_ROOM_ICONS: IconName[] = [
    'chat',
    'life',
    'fire',
    'sparkle',
    'theatre',
];

export function paletteFor(seed: string) {
    let hash = 0;
    for (let i = 0; i < seed.length; i++)
        hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
    return { icon: REAL_ROOM_ICONS[hash % REAL_ROOM_ICONS.length] };
}

export const NEW_MEMBERS: StubMember[] = [
    {
        id: 'm-jhon',
        name: 'Jhon Doe',
        handle: '@jhond',
        lastSeen: '1 min ago',
        avatarSeed: 'jhon',
        online: true,
    },
    {
        id: 'm-mary',
        name: 'Mary Perez',
        handle: '@maryp',
        lastSeen: '52 min ago',
        avatarSeed: 'mary',
        online: true,
    },
    {
        id: 'm-rebeca',
        name: 'Rebeca Smitt',
        handle: '@rebecas',
        lastSeen: '1 hour ago',
        avatarSeed: 'rebeca',
        online: false,
    },
];

export const RECENT_ACTIVITY: StubActivity[] = [
    {
        id: 'a-vannessa',
        name: 'Vannessa Sandoval',
        action: 'joined VR Enthusiasts',
        when: '23 min ago',
        avatarSeed: 'vannessa',
    },
    {
        id: 'a-michael',
        name: 'Michael Marshall',
        action: 'posted in 3D Art',
        when: '51 min ago',
        avatarSeed: 'michael',
    },
    {
        id: 'a-marina',
        name: 'Marina Lopez',
        action: 'created Lo-Fi & Chill',
        when: '3 hours ago',
        avatarSeed: 'marina',
    },
];

export const PROFILE_STUB = {
    name: 'Sophie Smitt',
    handle: '@sophiesol',
    avatarSeed: 'sophie',
    online: true,
};
