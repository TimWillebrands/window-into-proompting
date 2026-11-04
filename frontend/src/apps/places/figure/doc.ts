import type { MoveDoc } from './moves';
import type { PoseDoc } from './pose';

/** A Figure as a document an LLM can write: every aspect is numbers, names, hex colours, SVG or blobs. See ADR 0028. */

export type Hex = string;

export interface BodyDoc {
    skin: Hex;
    /** All 0..1, 0.5 = average. */
    height: number;
    build: number;
    /** 0 straight, 1 hourglass. */
    curve: number;
    shoulders: number;
    hips: number;
    legs: number;
    head: number;
}

export interface FaceDoc {
    /** 0 round, 0.5 oval, 1 long. */
    shape: number;
    jaw: number;
    nose: number;
    ears: number;
    eyes: {
        shape: 'round' | 'almond' | 'cat' | 'droopy' | 'wide';
        size: number;
        iris: Hex;
        /** Eyeliner weight, 0 = none. */
        liner: number;
        wing: number;
        linerColor: Hex;
        shadow?: Hex;
    };
    brows: {
        color: Hex;
        weight: number;
        arch: number;
        /** A slit in the left brow. */
        slit?: boolean;
    };
    mouth: {
        width: number;
        lips: number;
        color: Hex;
        /** -1 frown, 0 neutral, 1 smile; the resting set of the mouth. */
        set: number;
    };
    blush?: Hex;
    freckles?: number;
    facialHair?: 'stubble' | 'goatee' | 'beard' | 'moustache' | 'soul-patch';
    facialHairColor?: Hex;
    /** Metal in the face. */
    piercings?: (
        | 'brow'
        | 'nose'
        | 'septum'
        | 'lip'
        | 'snakebites'
        | 'labret'
    )[];
    /** Free drawing over the face: stars, tear tattoo, bindi. viewBox 0 0 256 256. */
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
    /** A named patch of scalp, or an explicit [lon, lat] box in degrees (lon 0 = front, 90 = their left). */
    at: HairZoneName | [number, number, number, number];
    count: number;
    /** Length in head heights. */
    len: number;
    /** Lock width in head heights. */
    width: number;
    /** 0 even ends, 1 very choppy. */
    jag: number;
    /** 0 lies flat, 1 stands straight out. */
    lift: number;
    /** 0 defies gravity, 1 hangs. */
    fall: number;
    /** -1 sweeps to their right, 1 to their left. */
    sweep: number;
    curl: number;
    color?: Hex;
    /** Colour bands root to tip: coontails. */
    bands?: Hex[];
}

export interface HairDoc {
    color: Hex;
    /** Dipped ends. */
    tips?: Hex;
    /** 0 bald, 1 full skullcap under the locks. */
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
    /** Repeat size, 0..1. */
    scale: number;
}

export interface PrintDoc {
    /** Inline SVG, viewBox 0 0 256 256. */
    svg: string;
    /** Where it goes on the garment. */
    at:
        | 'chest'
        | 'back'
        | 'belly'
        | 'sleeveL'
        | 'sleeveR'
        | 'thighL'
        | 'thighR';
    /** 0..1 of the panel width. */
    size: number;
}

export interface TopDoc {
    color: Hex;
    /** 0 strapless .. 0.25 tee .. 1 full length. */
    sleeve: number;
    /** 0 cropped .. 0.5 hip .. 1 thigh, past 1 a dress. */
    hem: number;
    /** 0 skintight, 1 huge. */
    fit: number;
    neck: Neckline;
    /** Trim: cuffs, hem, collar. */
    trim?: Hex;
    hood?: boolean;
    /** Unzipped, showing what's under. */
    open?: boolean;
    /** Mesh or fishnet: skin shows through. */
    sheer?: number;
    pattern?: PatternDoc;
    prints?: PrintDoc[];
    /** Sleeve in another colour (raglan, layered long sleeve). */
    sleeveColor?: Hex;
}

export interface BottomDoc {
    color: Hex;
    kind: 'pants' | 'skirt';
    /** 0 short .. 1 floor. */
    length: number;
    /** 0 skintight, 1 huge. */
    fit: number;
    /** Bootcut to wide. */
    flare: number;
    /** 0 hipster low .. 1 high. */
    rise: number;
    pleats?: number;
    trim?: Hex;
    pattern?: PatternDoc;
    prints?: PrintDoc[];
    /** Ripped knees. */
    rips?: number;
}

export interface LegwearDoc {
    color: Hex;
    /** 0 ankle .. 0.5 knee .. 1 thigh. */
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
    /** Toe cap and laces. */
    trim: Hex;
    /** Sole height, 0..1. */
    platform: number;
}

export interface BlobDoc {
    /** Head heights from the socket. Band straps round its limb; shell caps the skull down to at[1]. */
    shape: 'ball' | 'capsule' | 'ring' | 'band' | 'shell';
    at: [number, number, number];
    to?: [number, number, number];
    r: number;
    tube?: number;
    axis?: 'x' | 'y' | 'z';
    color: Hex;
    /** Polished: studs, chains, piercings. */
    metal?: boolean;
    /** Flatten hair above this blob's lower edge: hats. */
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
    /** Worn over the top. */
    outer?: TopDoc;
    /** Worn under the top: a long sleeve under a tee. */
    under?: TopDoc;
    bottom: BottomDoc;
    legwear?: LegwearDoc;
    shoes: ShoeDoc;
    extras: ExtraDoc[];
    /** How they stand: their posture, in pose words. */
    stance?: PoseDoc;
    /** Their own take on a pose: a signature dance, a particular wave. */
    moves?: Record<string, MoveDoc>;
}
