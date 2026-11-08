import type { Appearance } from '../../api/model/appearance';
import type { ShopItem } from '../../api/model/shopItem';
import { paletteHex } from '../places/iso/appearance';

/** Catalogue tabs, in shop-floor order. */
export const TABS: {
    id: string;
    label: string;
    icon: string;
    slots: string[];
}[] = [
    { id: 'all', label: 'Everything', icon: '✨', slots: [] },
    { id: 'top', label: 'Tops', icon: '👕', slots: ['top'] },
    { id: 'bottom', label: 'Bottoms', icon: '👖', slots: ['bottom'] },
    { id: 'shoes', label: 'Shoes', icon: '👟', slots: ['shoes', 'legwear'] },
    { id: 'accessory', label: 'Extras', icon: '🎩', slots: ['accessory'] },
    { id: 'hair', label: 'Salon', icon: '✂️', slots: ['hair', 'hairStyle'] },
];

export const inTab = (item: ShopItem, tab: string) => {
    const t = TABS.find((x) => x.id === tab);
    return !t || t.slots.length === 0 || t.slots.includes(item.slot);
};

/** Haircuts and dye are done to you, never owned or given. */
export const isService = (item: ShopItem) =>
    item.slot === 'hair' || item.slot === 'hairStyle';

const TOP: Record<string, string> = {
    tshirt: '👕',
    hoodie: '🧥',
    shirt: '👔',
    sweater: '🧶',
    jacket: '🧥',
    dress: '👗',
    tank: '🎽',
    suit: '🤵',
    'baby-tee': '👚',
    'band-tee': '🤘',
    'long-sleeve': '👕',
    mesh: '🕸️',
    corset: '🎀',
    flannel: '👔',
    tracksuit: '🧥',
    'feather-cloak': '🪶',
};
const BOTTOM: Record<string, string> = {
    trousers: '👖',
    jeans: '👖',
    shorts: '🩳',
    skirt: '👗',
    'skinny-jeans': '👖',
    'baggy-jeans': '👖',
    bootcut: '👖',
    cargo: '👖',
    'plaid-skirt': '👗',
    'mini-skirt': '👗',
};
const EXTRA: Record<string, string> = {
    glasses: '👓',
    sunglasses: '🕶️',
    hat: '🎩',
    cap: '🧢',
    beanie: '🧢',
    scarf: '🧣',
    headphones: '🎧',
    earrings: '💎',
    flower: '🌸',
    bow: '🎀',
    necklace: '📿',
    bandana: '🧣',
    choker: '📿',
    'studded-belt': '⛓️',
    wristbands: '🤘',
    'arm-warmers': '🧤',
    'wallet-chain': '⛓️',
    staff: '🪄',
    none: '🚫',
};
const SHOE: Record<string, string> = {
    boots: '🥾',
    platforms: '🥾',
    heels: '👠',
    sandals: '🩴',
    loafers: '👞',
};

/** The big icon on an item's tile. */
export function itemIcon(item: ShopItem): string {
    const p = item.picks ?? {};
    switch (item.slot) {
        case 'top':
            return TOP[p.topStyle ?? ''] ?? '👕';
        case 'bottom':
            return BOTTOM[p.bottomStyle ?? ''] ?? '👖';
        case 'shoes':
            return SHOE[p.shoeStyle ?? ''] ?? '👟';
        case 'legwear':
            return '🧦';
        case 'accessory':
            return EXTRA[p.accessory ?? ''] ?? '✨';
        case 'hairStyle':
            return '✂️';
        case 'hair':
            return '💇';
        default:
            return '🛍️';
    }
}

/** The swatch behind the icon: the item's own colour where it has one. */
export function itemColour(item: ShopItem): string {
    const p = item.picks ?? {};
    return paletteHex(p.top ?? p.bottom ?? p.shoes ?? p.hair, '#e8dff5');
}

/** Design slots a shop slot replaces, as the backend's FigureDesign does when you buy it. */
const REPLACES: Record<string, string[]> = {
    top: ['top', 'under', 'outer'],
    bottom: ['bottom'],
    shoes: ['shoes'],
    legwear: ['legwear'],
    hairStyle: ['hair'],
};

/** A look with items tried on over it; later items win per appearance key. */
export function tryOn(
    base: Appearance | null | undefined,
    items: ShopItem[],
): Appearance {
    const look: Record<string, unknown> = { ...(base ?? {}) };
    for (const item of items) Object.assign(look, item.picks ?? {});
    const slots = items.flatMap((i) => REPLACES[i.slot ?? ''] ?? []);
    if (base?.design && slots.length > 0) {
        const design = JSON.parse(base.design);
        for (const slot of slots) delete design[slot];
        look.design = JSON.stringify(design);
    }
    return look as Appearance;
}

/** One try-on per slot: picking another top swaps the top. */
export function toggleTry(tried: ShopItem[], item: ShopItem): ShopItem[] {
    if (tried.some((t) => t.key === item.key))
        return tried.filter((t) => t.key !== item.key);
    return [...tried.filter((t) => t.slot !== item.slot), item];
}
