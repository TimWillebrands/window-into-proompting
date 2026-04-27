export interface StubRoom {
    id: string;
    name: string;
    description: string;
    online: number;
    members: number;
    /** Tailwind classes including `dark:` variants. Used as part of `bg-gradient-to-br …`. */
    gradient: string;
    /** Tailwind classes for the small round badge (lighter chunk of same palette). */
    badgeGradient: string;
    /** emoji avatar for the round badge */
    emoji: string;
    /** category bucket */
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
    icon: string;
}

export const NAV_CATEGORIES: StubCategory[] = [
    { id: 'home', label: 'Home', icon: '🏠' },
    { id: 'music', label: 'Music', icon: '🎵' },
    { id: 'gaming', label: 'Gaming', icon: '🎮' },
    { id: 'education', label: 'Education', icon: '🎓' },
    { id: 'science', label: 'Science & Tech', icon: '⚛️' },
    { id: 'entertainment', label: 'Entertainment', icon: '🎬' },
    { id: 'student', label: 'Student Hubs', icon: '👥' },
];

export const SERVER_RAIL: StubCategory[] = [
    { id: 'planet', label: 'All', icon: '🪐' },
    { id: 'music', label: 'Music', icon: '🎵' },
    { id: 'gaming', label: 'Gaming', icon: '🎮' },
    { id: 'education', label: 'Education', icon: '🎓' },
    { id: 'science', label: 'Science', icon: '⚛️' },
    { id: 'entertainment', label: 'Entertainment', icon: '▶️' },
    { id: 'student', label: 'Student Hubs', icon: '👥' },
];

export const FEATURED_ROOMS: StubRoom[] = [
    {
        id: 'stub-vr',
        name: 'Virtual Reality',
        description:
            'A community for VR and novices alike, regular and friendly chat.',
        online: 5786,
        members: 45098,
        gradient:
            'from-violet-400 via-fuchsia-400 to-indigo-500 dark:from-violet-700 dark:via-fuchsia-700 dark:to-indigo-800',
        badgeGradient:
            'from-violet-300 to-indigo-400 dark:from-violet-600 dark:to-indigo-700',
        emoji: '🥽',
        category: 'featured',
    },
    {
        id: 'stub-gameplay',
        name: 'Game Play',
        description: 'Always a new challenge. Great place to make new friends.',
        online: 1786,
        members: 101098,
        gradient:
            'from-lime-400 via-emerald-400 to-green-600 dark:from-emerald-700 dark:via-green-700 dark:to-teal-800',
        badgeGradient:
            'from-lime-300 to-emerald-400 dark:from-emerald-600 dark:to-green-700',
        emoji: '🎮',
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
        gradient:
            'from-cyan-300 via-blue-400 to-indigo-500 dark:from-cyan-700 dark:via-blue-800 dark:to-indigo-900',
        badgeGradient:
            'from-cyan-300 to-indigo-400 dark:from-cyan-700 dark:to-indigo-800',
        emoji: '🎨',
        category: 'popular',
    },
    {
        id: 'stub-nft',
        name: 'NFT',
        description: 'An NFT community so that everyone can share their NFTs.',
        online: 233,
        members: 5093,
        gradient:
            'from-pink-300 via-orange-300 to-yellow-400 dark:from-pink-800 dark:via-orange-800 dark:to-amber-900',
        badgeGradient:
            'from-pink-300 to-orange-400 dark:from-pink-700 dark:to-orange-800',
        emoji: '🪙',
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
        gradient:
            'from-rose-500 via-red-500 to-rose-700 dark:from-rose-800 dark:via-red-900 dark:to-rose-950',
        badgeGradient:
            'from-rose-300 to-red-400 dark:from-rose-700 dark:to-red-800',
        emoji: '🎬',
        category: 'recent',
    },
    {
        id: 'stub-cosmos',
        name: 'Cosmos',
        description: 'Space, telescopes, and the occasional alien debate.',
        online: 142,
        members: 3304,
        gradient:
            'from-indigo-500 via-purple-600 to-fuchsia-700 dark:from-indigo-800 dark:via-purple-900 dark:to-fuchsia-950',
        badgeGradient:
            'from-indigo-300 to-fuchsia-400 dark:from-indigo-700 dark:to-fuchsia-800',
        emoji: '🌌',
        category: 'recent',
    },
    {
        id: 'stub-skies',
        name: 'Open Skies',
        description: 'Sunset photos, hiking, and outdoor talk.',
        online: 76,
        members: 989,
        gradient:
            'from-sky-300 via-cyan-300 to-emerald-300 dark:from-sky-800 dark:via-cyan-900 dark:to-emerald-900',
        badgeGradient:
            'from-sky-300 to-emerald-400 dark:from-sky-700 dark:to-emerald-800',
        emoji: '🏔️',
        category: 'recent',
    },
];

export const ALL_STUB_ROOMS: StubRoom[] = [
    ...FEATURED_ROOMS,
    ...POPULAR_ROOMS,
    ...RECENT_ROOMS,
];

/** Fixed gradient palette for "Your Rooms" — cycled deterministically per real chat-group. */
export const REAL_ROOM_GRADIENTS: Array<{
    gradient: string;
    badgeGradient: string;
    emoji: string;
}> = [
    {
        gradient:
            'from-fuchsia-400 via-pink-400 to-rose-500 dark:from-fuchsia-700 dark:via-pink-800 dark:to-rose-900',
        badgeGradient:
            'from-fuchsia-300 to-rose-400 dark:from-fuchsia-700 dark:to-rose-800',
        emoji: '💬',
    },
    {
        gradient:
            'from-emerald-400 via-teal-400 to-cyan-500 dark:from-emerald-700 dark:via-teal-800 dark:to-cyan-900',
        badgeGradient:
            'from-emerald-300 to-cyan-400 dark:from-emerald-700 dark:to-cyan-800',
        emoji: '🌿',
    },
    {
        gradient:
            'from-amber-300 via-orange-400 to-red-500 dark:from-amber-700 dark:via-orange-800 dark:to-red-900',
        badgeGradient:
            'from-amber-300 to-red-400 dark:from-amber-700 dark:to-red-800',
        emoji: '🔥',
    },
    {
        gradient:
            'from-indigo-400 via-blue-500 to-sky-600 dark:from-indigo-700 dark:via-blue-800 dark:to-sky-900',
        badgeGradient:
            'from-indigo-300 to-sky-400 dark:from-indigo-700 dark:to-sky-800',
        emoji: '✨',
    },
    {
        gradient:
            'from-purple-400 via-violet-500 to-indigo-600 dark:from-purple-700 dark:via-violet-800 dark:to-indigo-900',
        badgeGradient:
            'from-purple-300 to-indigo-400 dark:from-purple-700 dark:to-indigo-800',
        emoji: '🎭',
    },
];

export function paletteFor(seed: string) {
    let hash = 0;
    for (let i = 0; i < seed.length; i++)
        hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
    return REAL_ROOM_GRADIENTS[hash % REAL_ROOM_GRADIENTS.length];
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
