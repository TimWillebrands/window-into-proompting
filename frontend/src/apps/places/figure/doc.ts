import type { MoveDoc } from './moves';
import type { PoseDoc } from './pose';

export type Hex = string;

export interface BodyDoc {
    skin: Hex;
    height: number;
    build: number;
    curve: number;
    shoulders: number;
    hips: number;
    legs: number;
    head: number;
    muscle?: number;
    belly?: number;
}

export interface FaceDoc {
    shape: number;
    jaw: number;
    nose: number;
    ears: number;
    eyes: {
        shape: 'round' | 'almond' | 'cat' | 'droopy' | 'wide';
        size: number;
        iris: Hex;
        liner: number;
        wing: number;
        linerColor: Hex;
        shadow?: Hex;
    };
    brows: {
        color: Hex;
        weight: number;
        arch: number;
        slit?: boolean;
    };
    mouth: {
        width: number;
        lips: number;
        color: Hex;
        set: number;
    };
    blush?: Hex;
    freckles?: number;
    facialHair?: 'stubble' | 'goatee' | 'beard' | 'moustache' | 'soul-patch';
    facialHairColor?: Hex;
    piercings?: (
        | 'brow'
        | 'nose'
        | 'septum'
        | 'lip'
        | 'snakebites'
        | 'labret'
    )[];
    paint?: string;
}

export type HairZoneName =
    | 'crown'
    | 'top'
    | 'fringe'
    | 'sideL'
    | 'sideR'
    | 'back'
    | 'nape';

export interface HairZone {
    at: HairZoneName | [number, number, number, number];
    count: number;
    len: number;
    width: number;
    jag: number;
    lift: number;
    fall: number;
    sweep: number;
    curl: number;
    color?: Hex;
    bands?: Hex[];
}

export interface HairDoc {
    color: Hex;
    tips?: Hex;
    cap: number;
    zones: HairZone[];
}

export type Neckline = 'crew' | 'v' | 'scoop' | 'boat' | 'turtle' | 'collar';

export interface PatternDoc {
    kind:
        | 'stripes'
        | 'plaid'
        | 'checker'
        | 'dots'
        | 'fishnet'
        | 'fade'
        | 'stars';
    colors: Hex[];
    scale: number;
}

export interface PrintDoc {
    svg: string;
    at:
        | 'chest'
        | 'back'
        | 'belly'
        | 'sleeveL'
        | 'sleeveR'
        | 'thighL'
        | 'thighR';
    size: number;
}

export interface TopDoc {
    color: Hex;
    sleeve: number;
    hem: number;
    fit: number;
    neck: Neckline;
    trim?: Hex;
    hood?: boolean;
    open?: boolean;
    sheer?: number;
    pattern?: PatternDoc;
    prints?: PrintDoc[];
    sleeveColor?: Hex;
}

export interface BottomDoc {
    color: Hex;
    kind: 'pants' | 'skirt';
    length: number;
    fit: number;
    flare: number;
    rise: number;
    pleats?: number;
    trim?: Hex;
    pattern?: PatternDoc;
    prints?: PrintDoc[];
    rips?: number;
}

export interface LegwearDoc {
    color: Hex;
    length: number;
    pattern?: PatternDoc;
    sheer?: number;
}

export interface ShoeDoc {
    kind:
        | 'sneaker'
        | 'skate'
        | 'hi-top'
        | 'boot'
        | 'platform'
        | 'heel'
        | 'sandal'
        | 'loafer';
    color: Hex;
    sole: Hex;
    trim: Hex;
    platform: number;
}

export interface BlobDoc {
    shape: 'ball' | 'capsule' | 'ring' | 'band' | 'shell';
    at: [number, number, number];
    to?: [number, number, number];
    r: number;
    tube?: number;
    axis?: 'x' | 'y' | 'z';
    color: Hex;
    metal?: boolean;
    hat?: boolean;
}

export type Socket =
    | 'head'
    | 'face'
    | 'ear-l'
    | 'ear-r'
    | 'neck'
    | 'chest'
    | 'back'
    | 'waist'
    | 'hip-l'
    | 'hip-r'
    | 'wrist-l'
    | 'wrist-r'
    | 'forearm-l'
    | 'forearm-r'
    | 'hand-l'
    | 'hand-r'
    | 'ankle-l'
    | 'ankle-r';

export interface ExtraDoc {
    name: string;
    socket: Socket;
    blobs: BlobDoc[];
}

export interface FigureDoc {
    body: BodyDoc;
    face: FaceDoc;
    hair: HairDoc;
    top: TopDoc;
    outer?: TopDoc;
    under?: TopDoc;
    bottom: BottomDoc;
    legwear?: LegwearDoc;
    shoes: ShoeDoc;
    extras: ExtraDoc[];
    stance?: PoseDoc;
    moves?: Record<string, MoveDoc>;
}
