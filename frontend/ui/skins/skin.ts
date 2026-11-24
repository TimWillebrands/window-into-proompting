import type { WindowSize } from '../src/parts/chrome/WindowManager/WindowManager';

export type SkinRegion = {
    name: string;
    in: 'frame' | 'titlebar';
};

export type Skin = {
    name: string;
    label: string;
    titleBar: 'standard' | 'tall';
    maximise: boolean;
    minSize?: WindowSize;
    regions?: readonly SkinRegion[];
};

export const defineSkin = <const T extends Skin>(skin: T): T => skin;
