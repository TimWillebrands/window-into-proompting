import { msn } from './msn/msn';
import type { Skin } from './skin';

export const skins = { msn } as const satisfies Record<string, Skin>;

export type SkinName = keyof typeof skins;

export const skinNames = Object.keys(skins) as SkinName[];

export const skinOf = (name: SkinName | undefined): Skin | undefined =>
    name ? skins[name] : undefined;

export type { Skin, SkinRegion } from './skin';
