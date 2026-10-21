import type { Appearance } from '../../../api/model/appearance';

/** Palette names (backend AppearanceRules) → pixel colours. */
const SKIN: Record<string, string> = {
    light: '#f6d7bf',
    fair: '#f1c9a5',
    olive: '#d6a77a',
    tan: '#c68c5d',
    brown: '#9a6440',
    dark: '#5e3b24',
};
const HAIR: Record<string, string> = {
    black: '#1f1b18',
    brown: '#5a3a22',
    blonde: '#e3c36b',
    red: '#b2462a',
    grey: '#9a9a9a',
    white: '#eeeeee',
    blue: '#3b6fd6',
    pink: '#e57fb0',
    green: '#3f9b5a',
    purple: '#7b4fc4',
};
const CLOTH: Record<string, string> = {
    red: '#d9534f',
    orange: '#f0913a',
    yellow: '#f3d34a',
    green: '#5cb85c',
    teal: '#2aa198',
    blue: '#4a7fd6',
    navy: '#2c3e66',
    purple: '#8e5cc4',
    pink: '#ec8fb5',
    black: '#2b2b2b',
    white: '#f2f2f2',
    grey: '#8a8f99',
    brown: '#7a5233',
    beige: '#d8c6a3',
    olive: '#6b7a3a',
    maroon: '#6e2233',
};
const EYES: Record<string, string> = {
    dark: '#1d1d1d',
    brown: '#5a3415',
    blue: '#2f6fd1',
    green: '#2f8f4e',
    grey: '#6b7280',
};

export interface Look {
    skin: string;
    hair: string;
    hairStyle: string;
    top: string;
    bottom: string;
    accessory: string;
    /** tshirt, hoodie, shirt, sweater, jacket, dress, tank, suit. */
    topStyle: string;
    /** trousers, jeans, shorts, skirt. */
    bottomStyle: string;
    shoes: string;
    facialHair: string;
    build: string;
    height: string;
    eyes: string;
}

export const lookOf = (
    a: Appearance | null | undefined,
    fallbackTop: string,
): Look => ({
    skin: SKIN[a?.skin ?? ''] ?? SKIN.fair,
    hair: HAIR[a?.hair ?? ''] ?? HAIR.brown,
    hairStyle: a?.hairStyle ?? 'short',
    top: CLOTH[a?.top ?? ''] ?? fallbackTop,
    bottom: CLOTH[a?.bottom ?? ''] ?? '#3b3f58',
    accessory: a?.accessory ?? 'none',
    topStyle: a?.topStyle ?? 'tshirt',
    bottomStyle: a?.bottomStyle ?? 'trousers',
    shoes: CLOTH[a?.shoes ?? ''] ?? '#2b2b2b',
    facialHair: a?.facialHair ?? 'none',
    build: a?.build ?? 'average',
    height: a?.height ?? 'average',
    eyes: EYES[a?.eyes ?? ''] ?? EYES.dark,
});

/** Hair and accessory over a head of radius r at (x, y). */
export function drawHead(
    ctx: CanvasRenderingContext2D,
    look: Look,
    x: number,
    y: number,
    r: number,
) {
    ctx.fillStyle = look.hair;
    switch (look.hairStyle) {
        case 'bald':
            break;
        case 'long':
            ctx.fillRect(x - r, y - r * 0.4, r * 2, r * 1.9);
            break;
        case 'curly':
            for (const dx of [-0.8, -0.3, 0.3, 0.8]) {
                ctx.beginPath();
                ctx.arc(x + dx * r, y - r * 0.7, r * 0.45, 0, Math.PI * 2);
                ctx.fill();
            }
            break;
        case 'bun':
            ctx.beginPath();
            ctx.arc(x, y - r * 1.25, r * 0.45, 0, Math.PI * 2);
            ctx.fill();
            break;
        case 'mohawk':
            ctx.fillRect(x - r * 0.2, y - r * 1.5, r * 0.4, r * 1.1);
            break;
    }
    ctx.fillStyle = look.skin;
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = look.hair;
    if (look.hairStyle !== 'bald' && look.hairStyle !== 'mohawk') {
        ctx.beginPath();
        ctx.arc(x, y - r * 0.15, r, Math.PI, Math.PI * 2);
        ctx.fill();
    }
    ctx.fillStyle = '#222';
    ctx.fillRect(x - r * 0.45, y + r * 0.1, r * 0.2, r * 0.2);
    ctx.fillRect(x + r * 0.25, y + r * 0.1, r * 0.2, r * 0.2);
    switch (look.accessory) {
        case 'glasses':
            ctx.strokeStyle = '#222';
            ctx.lineWidth = Math.max(1, r * 0.15);
            ctx.strokeRect(x - r * 0.65, y, r * 0.5, r * 0.35);
            ctx.strokeRect(x + r * 0.15, y, r * 0.5, r * 0.35);
            ctx.lineWidth = 1;
            break;
        case 'hat':
            ctx.fillStyle = '#4a3b2a';
            ctx.fillRect(x - r * 1.3, y - r * 0.75, r * 2.6, r * 0.3);
            ctx.fillRect(x - r * 0.8, y - r * 1.5, r * 1.6, r * 0.8);
            break;
        case 'beanie':
            ctx.fillStyle = '#c0392b';
            ctx.beginPath();
            ctx.arc(x, y - r * 0.3, r * 1.05, Math.PI, Math.PI * 2);
            ctx.fill();
            break;
        case 'headphones':
            ctx.strokeStyle = '#333';
            ctx.lineWidth = Math.max(1, r * 0.25);
            ctx.beginPath();
            ctx.arc(x, y, r * 1.1, Math.PI * 1.1, Math.PI * 1.9);
            ctx.stroke();
            ctx.lineWidth = 1;
            ctx.fillStyle = '#333';
            ctx.fillRect(x - r * 1.25, y - r * 0.2, r * 0.4, r * 0.6);
            ctx.fillRect(x + r * 0.85, y - r * 0.2, r * 0.4, r * 0.6);
            break;
    }
}

/** A scarf sits on the shoulders, drawn after the torso. */
export function drawScarf(
    ctx: CanvasRenderingContext2D,
    look: Look,
    x: number,
    y: number,
    w: number,
    h: number,
) {
    if (look.accessory !== 'scarf') return;
    ctx.fillStyle = '#e8a33c';
    ctx.fillRect(x - w / 2, y, w, h);
}
