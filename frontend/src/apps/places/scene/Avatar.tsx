import { useFrame } from '@react-three/fiber';
import { useMemo, useRef } from 'react';
import * as THREE from 'three';
import type { Look } from '../iso/appearance';
import type { Pt } from '../iso/iso';
import type { Pose } from './motion';

const S = 1.1;
const HIP = 0.34;
const THIGH = 0.17;
const SHIN = 0.13;
const UPPER = 0.15;
const FORE = 0.15;
const TORSO = 0.36;

export interface AvatarState {
    pose: Pose;
    /** World height under the feet. */
    ground: number;
    asleep: boolean;
    lying: boolean;
    phone: boolean;
    talking: boolean;
    /** Hang-out pose from the backend (lean, lounge, perch, dance, lie, cross-legged, think, wave, hug, slow-dance, high-five, hold-hands); null = plain. */
    hangout?: string | null;
    /** World tile of the pair-pose partner, to face and step toward. */
    partner?: Pt | null;
}

interface Props {
    look: Look;
    isMe: boolean;
    /** Read every frame; no React re-render while walking. */
    state: () => AvatarState | null;
    onClick?: () => void;
    children?: React.ReactNode;
}

const turnTo = (from: number, to: number, k: number) => {
    let d = to - from;
    while (d > Math.PI) d -= Math.PI * 2;
    while (d < -Math.PI) d += Math.PI * 2;
    return from + d * k;
};

const mats = new Map<string, THREE.Material>();
/** Shared flat-shaded material per colour (and opacity). */
function mat(c: string, opacity = 1): THREE.Material {
    const k = `${c}:${opacity}`;
    let m = mats.get(k);
    if (!m) {
        m = new THREE.MeshStandardMaterial({
            color: c,
            flatShading: true,
            roughness: 0.85,
            transparent: opacity < 1,
            opacity,
        });
        mats.set(k, m);
    }
    return m;
}

const meRing = new THREE.RingGeometry(0.32, 0.4, 24);
const meRingMat = new THREE.MeshBasicMaterial({
    color: '#3b82f6',
    transparent: true,
    opacity: 0.8,
});
const blob = new THREE.CircleGeometry(0.24, 16);
const blobMat = new THREE.MeshBasicMaterial({
    color: '#000000',
    transparent: true,
    opacity: 0.18,
    depthWrite: false,
});

/** One of each kind of material an avatar draws with, to compile before anyone walks in. */
export const avatarMaterials = (): THREE.Material[] => [
    mat('#808080'),
    mat('#808080', 0.5),
    meRingMat,
    blobMat,
];

const geos = new Map<string, THREE.BufferGeometry>();
/** Shared geometry by shape and size. */
function geo(kind: string, ...a: number[]): THREE.BufferGeometry {
    const k = `${kind}:${a.join(',')}`;
    let g = geos.get(k);
    if (!g) {
        g =
            kind === 'ball'
                ? new THREE.IcosahedronGeometry(a[0], a[1] ?? 0)
                : kind === 'cyl'
                  ? new THREE.CylinderGeometry(a[0], a[1], a[2], a[3] ?? 8)
                  : kind === 'cone'
                    ? new THREE.ConeGeometry(a[0], a[1], a[2] ?? 6)
                    : kind === 'torus'
                      ? new THREE.TorusGeometry(
                            a[0],
                            a[1],
                            5,
                            12,
                            a[2] ?? Math.PI * 2,
                        )
                      : new THREE.BoxGeometry(a[0], a[1], a[2]);
        geos.set(k, g);
    }
    return g;
}

type V3 = [number, number, number];

/** A box (w, h, d) centred at p. */
function B({
    s,
    p,
    c,
    o = 1,
    r,
}: {
    s: V3;
    p: V3;
    c: string;
    o?: number;
    r?: V3;
}) {
    return (
        <mesh
            geometry={geo('box', ...s)}
            material={mat(c, o)}
            position={p}
            rotation={r}
            castShadow
        />
    );
}

function shade(hex: string, k: number): string {
    const c = new THREE.Color(hex);
    if (k < 0) c.lerp(new THREE.Color('#000000'), -k);
    else c.lerp(new THREE.Color('#ffffff'), k);
    return `#${c.getHexString()}`;
}

/** Joint angles and offsets for one frame; blended toward each frame so poses don't snap. */
interface Rig {
    hipL: number;
    hipLz: number;
    kneeL: number;
    hipR: number;
    hipRz: number;
    kneeR: number;
    shL: number;
    shLz: number;
    elL: number;
    shR: number;
    shRz: number;
    elR: number;
    torsoX: number;
    torsoZ: number;
    headX: number;
    headY: number;
    bob: number;
    y: number;
    turn: number;
}

const ZERO: Rig = {
    hipL: 0,
    hipLz: 0,
    kneeL: 0,
    hipR: 0,
    hipRz: 0,
    kneeR: 0,
    shL: 0,
    shLz: -0.06,
    elL: 0,
    shR: 0,
    shRz: 0.06,
    elR: 0,
    torsoX: 0,
    torsoZ: 0,
    headX: 0,
    headY: 0,
    bob: 0,
    y: 0,
    turn: 0,
};

const SITTING = new Set(['lounge', 'perch', 'cross-legged']);
const PAIR = new Set(['hug', 'slow-dance', 'high-five', 'hold-hands']);

/** A blocky little person in their own colours and clothes. Walks, sits, lies down, holds a phone, strikes hang-out poses. */
export default function Avatar({
    look,
    isMe,
    state,
    onClick,
    children,
}: Props) {
    const root = useRef<THREE.Group>(null);
    const body = useRef<THREE.Group>(null);
    const torso = useRef<THREE.Group>(null);
    const legL = useRef<THREE.Group>(null);
    const legR = useRef<THREE.Group>(null);
    const kneeL = useRef<THREE.Group>(null);
    const kneeR = useRef<THREE.Group>(null);
    const armL = useRef<THREE.Group>(null);
    const armR = useRef<THREE.Group>(null);
    const elbowL = useRef<THREE.Group>(null);
    const elbowR = useRef<THREE.Group>(null);
    const head = useRef<THREE.Group>(null);
    const eyes = useRef<THREE.Group>(null);
    const mouth = useRef<THREE.Mesh>(null);
    const phone = useRef<THREE.Mesh>(null);
    const facing = useRef(0);
    const rig = useRef<Rig>({ ...ZERO });
    const seed = useMemo(() => Math.random() * 10, []);
    const wide =
        look.build === 'slim' ? 0.88 : look.build === 'broad' ? 1.15 : 1;
    const tall =
        look.height === 'short' ? 0.9 : look.height === 'tall' ? 1.1 : 1;
    const sy = S * tall;

    useFrame(({ clock }, dt) => {
        const s = state();
        const g = root.current;
        if (!g || !body.current) return;
        g.visible = !!s;
        if (!s) return;
        const t = clock.elapsedTime + seed;
        const { pose } = s;
        const moving = pose.moving;
        const named = !moving && !s.asleep ? (s.hangout ?? null) : null;
        const lying = !moving && (s.lying || named === 'lie');
        const seat = pose.slot?.seat ? pose.slot.h : undefined;
        const sitting =
            !moving &&
            !lying &&
            (seat !== undefined || (named !== null && SITTING.has(named)));

        // Facing: the way you walk, your partner, or your slot.
        let target = facing.current;
        const partner = named && PAIR.has(named) ? s.partner : null;
        if (moving && pose.toward) {
            const dx = pose.toward.x - pose.tile.x;
            const dz = pose.toward.y - pose.tile.y;
            if (Math.abs(dx) + Math.abs(dz) > 0.001)
                target = Math.atan2(dx, dz);
        } else if (partner) {
            const dx = partner.x - pose.tile.x;
            const dz = partner.y - pose.tile.y;
            const to = Math.atan2(dx, dz);
            // Holding hands: side by side, both toward the camera.
            target =
                named === 'hold-hands'
                    ? Math.sin(to + Math.PI / 2) + Math.cos(to + Math.PI / 2) >=
                      0
                        ? to + Math.PI / 2
                        : to - Math.PI / 2
                    : to;
        } else if (pose.slot?.face !== undefined)
            target = (pose.slot.face * Math.PI) / 2;
        facing.current = turnTo(facing.current, target, moving ? 0.35 : 0.12);
        const f = facing.current;

        // Where the feet go.
        let x = pose.tile.x + 0.5;
        let z = pose.tile.y + 0.5;
        let y = s.ground;
        if (sitting) {
            const hips = seat ?? s.ground + (named === 'perch' ? 0.5 : 0.1);
            y = hips - HIP * sy + 0.04;
        }
        if (lying) {
            if (seat !== undefined) {
                y = seat + 0.12;
                x += Math.sin(f) * 0.9;
                z += Math.cos(f) * 0.9;
            } else {
                y = s.ground + 0.15;
                x += Math.sin(f) * 0.5;
                z += Math.cos(f) * 0.5;
            }
        }
        if (partner && (named === 'hug' || named === 'slow-dance')) {
            x += Math.sin(f) * 0.34;
            z += Math.cos(f) * 0.34;
        }

        // Joint targets.
        const T: Rig = { ...ZERO, y };
        const swing = moving ? Math.sin(t * 13) * 0.6 : 0;
        if (moving) {
            T.hipL = swing;
            T.hipR = -swing;
            T.kneeL = Math.max(0, -swing) * 0.9;
            T.kneeR = Math.max(0, swing) * 0.9;
            T.shL = -swing * 0.8;
            T.shR = swing * 0.8;
            T.elL = -0.3;
            T.elR = -0.3;
            T.bob = Math.abs(Math.sin(t * 13)) * 0.04;
        } else {
            T.bob = Math.sin(t * 1.6) * 0.005;
            T.headY = Math.sin(t * 0.4) * 0.25;
        }
        if (sitting) {
            T.hipL = -Math.PI / 2;
            T.hipR = -Math.PI / 2;
            T.kneeL = Math.PI / 2;
            T.kneeR = Math.PI / 2;
            T.shL = -0.25;
            T.shR = -0.25;
            T.elL = -0.5;
            T.elR = -0.5;
        }
        switch (named) {
            case 'lean':
                T.torsoX = 0.3;
                T.shL = -1.25;
                T.shR = -1.25;
                T.elL = -0.7;
                T.elR = -0.7;
                T.headX = -0.2;
                break;
            case 'lounge':
                T.torsoX = -0.45;
                T.hipL = -1.25;
                T.hipR = -1.15;
                T.kneeL = 0.2;
                T.kneeR = 0.35;
                T.hipLz = 0.1;
                T.hipRz = -0.1;
                T.shR = -2.7;
                T.shRz = -0.2;
                T.elR = -2.3;
                T.shL = -0.3;
                T.shLz = -0.4;
                T.elL = -0.2;
                T.headX = 0.15;
                T.headY = 0;
                break;
            case 'perch':
                T.kneeL = Math.PI / 2 + 0.25 * Math.sin(t * 2.2);
                T.kneeR = Math.PI / 2 + 0.25 * Math.sin(t * 2.2 + 1.7);
                T.shL = -0.1;
                T.shLz = -0.35;
                T.shR = -0.1;
                T.shRz = 0.35;
                T.elL = 0;
                T.elR = 0;
                T.torsoX = 0.08;
                break;
            case 'cross-legged':
                T.hipL = -Math.PI / 2;
                T.hipR = -Math.PI / 2;
                T.hipLz = -0.95;
                T.hipRz = 0.95;
                T.kneeL = 2.5;
                T.kneeR = 2.5;
                T.shL = -0.8;
                T.shR = -0.8;
                T.elL = -0.3;
                T.elR = -0.3;
                break;
            case 'dance': {
                const beat = t * 6;
                T.bob = Math.abs(Math.sin(beat)) * 0.08;
                T.torsoZ = Math.sin(beat / 2) * 0.18;
                T.hipL = Math.sin(beat) * 0.35;
                T.hipR = -Math.sin(beat) * 0.35;
                T.kneeL = 0.3 + Math.max(0, Math.sin(beat)) * 0.4;
                T.kneeR = 0.3 + Math.max(0, -Math.sin(beat)) * 0.4;
                T.shL = -2.6 + Math.sin(beat) * 0.4;
                T.shR = -2.6 - Math.sin(beat) * 0.4;
                T.shLz = -0.45;
                T.shRz = 0.45;
                T.elL = -0.5 + Math.sin(beat) * 0.4;
                T.elR = -0.5 - Math.sin(beat) * 0.4;
                T.headX = Math.sin(beat) * 0.15;
                T.headY = Math.sin(beat / 2) * 0.3;
                break;
            }
            case 'think':
                T.shR = -0.9;
                T.shRz = -0.35;
                T.elR = -2.3;
                T.shL = -0.55;
                T.shLz = 0.45;
                T.elL = -1.3;
                T.headX = 0.12;
                T.headY = 0.2;
                break;
            case 'wave':
                T.shR = -0.2;
                T.shRz = 2.7;
                T.elR = -0.2 + Math.sin(t * 9) * 0.45;
                break;
            case 'hug':
                T.shL = -1.35;
                T.shR = -1.35;
                T.shLz = 0.2;
                T.shRz = -0.2;
                T.elL = -1.25;
                T.elR = -1.25;
                T.headY = 0.35;
                T.headX = 0.1;
                break;
            case 'slow-dance':
                T.shL = -1.25;
                T.shR = -1.25;
                T.shLz = 0.45;
                T.shRz = -0.45;
                T.elL = -0.7;
                T.elR = -0.7;
                T.torsoZ = Math.sin(t * 1.4) * 0.08;
                T.turn = Math.sin(t * 0.7) * 0.25;
                T.bob = Math.abs(Math.sin(t * 1.4)) * 0.02;
                T.headY = 0.3;
                break;
            case 'high-five':
                T.shR = -2.8;
                T.shRz = -0.1;
                T.elR = -0.15;
                T.bob = Math.abs(Math.sin(t * 5)) * 0.05;
                break;
            case 'hold-hands': {
                // The hand toward the partner reaches out a little.
                const dx = (partner?.x ?? 0) - pose.tile.x;
                const dz = (partner?.y ?? 0) - pose.tile.y;
                const right = Math.cos(f) * dx - Math.sin(f) * dz;
                if (right > 0) {
                    T.shR = -0.25;
                    T.shRz = 0.55;
                } else {
                    T.shL = -0.25;
                    T.shLz = -0.55;
                }
                T.headY = right > 0 ? 0.4 : -0.4;
                break;
            }
        }
        if (s.phone && !lying && !named) {
            T.shR = -1.1;
            T.shRz = 0.25;
            T.elR = -0.6;
            T.headX = 0.35;
            T.headY = 0;
        }
        if (s.talking && !named) T.headX += Math.sin(t * 9) * 0.08;
        if (lying) T.headY = 0;

        // Blend toward the targets (walking stays crisp).
        const c = rig.current;
        const k = moving ? 1 : 1 - Math.exp(-Math.min(dt, 0.1) * 8);
        for (const key of Object.keys(T) as (keyof Rig)[])
            c[key] += (T[key] - c[key]) * k;
        if (moving || Math.abs(c.y - y) > 1) c.y = y;

        g.position.set(x, c.y, z);
        g.rotation.set(0, f + c.turn, 0);
        body.current.rotation.x = lying ? -Math.PI / 2 : 0;
        body.current.position.y = c.bob;
        legL.current?.rotation.set(c.hipL, 0, c.hipLz);
        legR.current?.rotation.set(c.hipR, 0, c.hipRz);
        kneeL.current?.rotation.set(c.kneeL, 0, 0);
        kneeR.current?.rotation.set(c.kneeR, 0, 0);
        armL.current?.rotation.set(c.shL, 0, c.shLz);
        armR.current?.rotation.set(c.shR, 0, c.shRz);
        elbowL.current?.rotation.set(c.elL, 0, 0);
        elbowR.current?.rotation.set(c.elR, 0, 0);
        torso.current?.rotation.set(c.torsoX, 0, c.torsoZ);
        head.current?.rotation.set(c.headX, c.headY, 0);
        if (phone.current) phone.current.visible = s.phone && !lying && !named;
        // Blink every few seconds; talk with the mouth.
        if (eyes.current)
            eyes.current.scale.y =
                (t * 0.37) % 1 < 0.035 || s.asleep ? 0.12 : 1;
        if (mouth.current)
            mouth.current.scale.y = s.talking
                ? 1 + Math.abs(Math.sin(t * 14)) * 2.5
                : 1;
    });

    const top = look.top;
    const dark = shade(top, -0.22);
    const longSleeves = !['tshirt', 'tank', 'dress'].includes(look.topStyle);
    const upperArm = look.topStyle === 'tank' ? look.skin : top;
    const foreArm = longSleeves ? top : look.skin;
    const legsBare = look.topStyle === 'dress' || look.bottomStyle === 'skirt';
    const thighColour = legsBare ? look.skin : look.bottom;
    const shinColour =
        legsBare || look.bottomStyle === 'shorts' ? look.skin : look.bottom;

    const leg = (
        side: -1 | 1,
        hip: React.RefObject<THREE.Group | null>,
        knee: React.RefObject<THREE.Group | null>,
    ) => (
        <group ref={hip} position={[side * 0.075, HIP, 0]}>
            <B s={[0.13, THIGH, 0.14]} p={[0, -THIGH / 2, 0]} c={thighColour} />
            {look.bottomStyle === 'jeans' ? (
                <B
                    s={[0.012, THIGH, 0.145]}
                    p={[side * 0.06, -THIGH / 2, 0]}
                    c={shade(look.bottom, -0.3)}
                />
            ) : null}
            <group ref={knee} position={[0, -THIGH, 0]}>
                <B
                    s={[0.125, SHIN, 0.135]}
                    p={[0, -SHIN / 2 + 0.01, 0]}
                    c={shinColour}
                />
                {look.bottomStyle === 'jeans' ? (
                    <B
                        s={[0.012, SHIN, 0.14]}
                        p={[side * 0.058, -SHIN / 2, 0]}
                        c={shade(look.bottom, -0.3)}
                    />
                ) : null}
                <B
                    s={[0.14, 0.06, 0.19]}
                    p={[0, -SHIN + 0.02, 0.025]}
                    c={look.shoes}
                />
                <B
                    s={[0.142, 0.015, 0.192]}
                    p={[0, -SHIN - 0.005, 0.025]}
                    c={shade(look.shoes, 0.5)}
                />
            </group>
        </group>
    );

    const arm = (
        side: -1 | 1,
        shoulder: React.RefObject<THREE.Group | null>,
        elbow: React.RefObject<THREE.Group | null>,
        withPhone: boolean,
    ) => (
        <group ref={shoulder} position={[side * 0.225, TORSO - 0.04, 0]}>
            <B
                s={[0.1, UPPER, 0.115]}
                p={[0, -UPPER / 2 + 0.02, 0]}
                c={upperArm}
            />
            <group ref={elbow} position={[0, -UPPER + 0.02, 0]}>
                <B s={[0.095, FORE, 0.11]} p={[0, -FORE / 2, 0]} c={foreArm} />
                {look.topStyle === 'sweater' ? (
                    <B
                        s={[0.1, 0.03, 0.115]}
                        p={[0, -FORE + 0.01, 0]}
                        c={dark}
                    />
                ) : null}
                <B
                    s={[0.085, 0.07, 0.09]}
                    p={[0, -FORE - 0.03, 0]}
                    c={look.skin}
                />
                {withPhone ? (
                    <mesh
                        ref={phone}
                        position={[0, -FORE - 0.06, 0.06]}
                        rotation={[0.3, 0, 0]}
                    >
                        <boxGeometry args={[0.07, 0.12, 0.02]} />
                        <meshBasicMaterial color="#9cc7ff" />
                    </mesh>
                ) : null}
            </group>
        </group>
    );

    return (
        // biome-ignore lint/a11y/noStaticElementInteractions: a three.js group, not DOM
        <group
            ref={root}
            // Hidden until its first frame places it, so its label doesn't flash at the origin.
            visible={false}
            onClick={(e) => {
                e.stopPropagation();
                onClick?.();
            }}
        >
            {isMe ? (
                <mesh
                    rotation={[-Math.PI / 2, 0, 0]}
                    position={[0, 0.02, 0]}
                    geometry={meRing}
                    material={meRingMat}
                />
            ) : null}
            <mesh
                rotation={[-Math.PI / 2, 0, 0]}
                position={[0, 0.01, 0]}
                geometry={blob}
                material={blobMat}
            />
            <group scale={[S * wide, sy, S * wide]}>
                <group ref={body}>
                    {leg(-1, legL, kneeL)}
                    {leg(1, legR, kneeR)}
                    {look.topStyle === 'dress' ? (
                        <mesh
                            geometry={geo('cyl', 0.17, 0.27, 0.26, 8)}
                            material={mat(top)}
                            position={[0, HIP - 0.11, 0]}
                            castShadow
                        />
                    ) : look.bottomStyle === 'skirt' ? (
                        <mesh
                            geometry={geo('cyl', 0.17, 0.24, 0.18, 8)}
                            material={mat(look.bottom)}
                            position={[0, HIP - 0.07, 0]}
                            castShadow
                        />
                    ) : null}
                    <group ref={torso} position={[0, HIP, 0]}>
                        <Torso look={look} dark={dark} />
                        {arm(-1, armL, elbowL, false)}
                        {arm(1, armR, elbowR, true)}
                        <group ref={head} position={[0, TORSO, 0]}>
                            <B
                                s={[0.1, 0.05, 0.1]}
                                p={[0, 0.0, 0]}
                                c={look.skin}
                            />
                            <B
                                s={[0.3, 0.28, 0.28]}
                                p={[0, 0.15, 0]}
                                c={look.skin}
                            />
                            <B
                                s={[0.035, 0.035, 0.03]}
                                p={[0, 0.13, 0.15]}
                                c={shade(look.skin, -0.12)}
                            />
                            <group ref={eyes} position={[0, 0.165, 0.141]}>
                                <B
                                    s={[0.05, 0.058, 0.012]}
                                    p={[-0.07, 0, 0]}
                                    c="#ffffff"
                                />
                                <B
                                    s={[0.05, 0.058, 0.012]}
                                    p={[0.07, 0, 0]}
                                    c="#ffffff"
                                />
                                <B
                                    s={[0.03, 0.045, 0.012]}
                                    p={[-0.068, -0.004, 0.004]}
                                    c={look.eyes}
                                />
                                <B
                                    s={[0.03, 0.045, 0.012]}
                                    p={[0.072, -0.004, 0.004]}
                                    c={look.eyes}
                                />
                            </group>
                            <B
                                s={[0.065, 0.014, 0.012]}
                                p={[-0.07, 0.215, 0.142]}
                                c={shade(look.hair, -0.1)}
                                r={[0, 0, 0.08]}
                            />
                            <B
                                s={[0.065, 0.014, 0.012]}
                                p={[0.07, 0.215, 0.142]}
                                c={shade(look.hair, -0.1)}
                                r={[0, 0, -0.08]}
                            />
                            <mesh
                                ref={mouth}
                                geometry={geo('box', 0.065, 0.014, 0.01)}
                                material={mat('#7a3b2e')}
                                position={[0, 0.075, 0.141]}
                            />
                            <FacialHair look={look} />
                            <Hair look={look} />
                            <Accessory look={look} />
                        </group>
                    </group>
                </group>
            </group>
            {children}
        </group>
    );
}

/** The top half, by style. Torso spans y 0..TORSO from the hips. */
function Torso({ look, dark }: { look: Look; dark: string }) {
    const top = look.top;
    const w = 0.34;
    const d = 0.2;
    const front = d / 2 + 0.006;
    const parts: React.ReactNode[] = [
        <B key="t" s={[w, TORSO, d]} p={[0, TORSO / 2, 0]} c={top} />,
    ];
    switch (look.topStyle) {
        case 'hoodie':
            parts.push(
                <B
                    key="hood"
                    s={[0.28, 0.13, 0.09]}
                    p={[0, TORSO + 0.01, -0.11]}
                    c={dark}
                />,
                <B
                    key="pocket"
                    s={[0.2, 0.08, 0.012]}
                    p={[0, 0.08, front]}
                    c={dark}
                />,
                <B
                    key="s1"
                    s={[0.012, 0.09, 0.01]}
                    p={[-0.04, TORSO - 0.08, front]}
                    c="#f2f2f2"
                />,
                <B
                    key="s2"
                    s={[0.012, 0.09, 0.01]}
                    p={[0.04, TORSO - 0.08, front]}
                    c="#f2f2f2"
                />,
            );
            break;
        case 'shirt':
            parts.push(
                <B
                    key="c1"
                    s={[0.09, 0.05, 0.012]}
                    p={[-0.05, TORSO - 0.03, front]}
                    c={shade(top, 0.35)}
                    r={[0, 0, 0.5]}
                />,
                <B
                    key="c2"
                    s={[0.09, 0.05, 0.012]}
                    p={[0.05, TORSO - 0.03, front]}
                    c={shade(top, 0.35)}
                    r={[0, 0, -0.5]}
                />,
                ...[0.24, 0.16, 0.08].map((y) => (
                    <B
                        key={`b${y}`}
                        s={[0.02, 0.02, 0.01]}
                        p={[0, y, front]}
                        c={shade(top, 0.6)}
                    />
                )),
            );
            break;
        case 'sweater':
            parts.push(
                <B
                    key="hem"
                    s={[w + 0.01, 0.05, d + 0.01]}
                    p={[0, 0.025, 0]}
                    c={dark}
                />,
                <B
                    key="neck"
                    s={[0.14, 0.03, 0.12]}
                    p={[0, TORSO, 0]}
                    c={dark}
                />,
            );
            break;
        case 'jacket':
        case 'suit':
            parts.push(
                <B
                    key="inner"
                    s={[0.1, TORSO - 0.04, 0.01]}
                    p={[0, TORSO / 2 + 0.02, front]}
                    c={look.topStyle === 'suit' ? '#f4f1ea' : '#e9e4d8'}
                />,
                <B
                    key="l1"
                    s={[0.05, 0.15, 0.012]}
                    p={[-0.07, TORSO - 0.09, front + 0.002]}
                    c={dark}
                    r={[0, 0, -0.3]}
                />,
                <B
                    key="l2"
                    s={[0.05, 0.15, 0.012]}
                    p={[0.07, TORSO - 0.09, front + 0.002]}
                    c={dark}
                    r={[0, 0, 0.3]}
                />,
            );
            if (look.topStyle === 'suit')
                parts.push(
                    <B
                        key="tie"
                        s={[0.04, 0.2, 0.012]}
                        p={[0, TORSO - 0.13, front + 0.006]}
                        c="#8b1e2d"
                    />,
                    <B
                        key="knot"
                        s={[0.05, 0.035, 0.014]}
                        p={[0, TORSO - 0.025, front + 0.006]}
                        c="#6d1622"
                    />,
                );
            break;
        case 'tank':
            parts.push(
                <B
                    key="st1"
                    s={[0.05, 0.02, d + 0.01]}
                    p={[-0.12, TORSO, 0]}
                    c={top}
                />,
                <B
                    key="st2"
                    s={[0.05, 0.02, d + 0.01]}
                    p={[0.12, TORSO, 0]}
                    c={top}
                />,
                <B
                    key="neck"
                    s={[0.12, 0.05, 0.012]}
                    p={[0, TORSO - 0.025, front]}
                    c={look.skin}
                />,
            );
            break;
        case 'dress':
            parts.push(
                <B
                    key="belt"
                    s={[w + 0.01, 0.03, d + 0.01]}
                    p={[0, 0.04, 0]}
                    c={dark}
                />,
            );
            break;
        default:
            parts.push(
                <B
                    key="neck"
                    s={[0.12, 0.03, 0.012]}
                    p={[0, TORSO - 0.015, front]}
                    c={dark}
                />,
            );
    }
    if (look.accessory === 'scarf')
        parts.push(
            <B
                key="scarf"
                s={[0.36, 0.08, 0.24]}
                p={[0, TORSO - 0.02, 0]}
                c="#e8a33c"
            />,
            <B
                key="tail"
                s={[0.07, 0.16, 0.03]}
                p={[0.07, TORSO - 0.12, front + 0.02]}
                c="#e8a33c"
            />,
        );
    if (look.accessory === 'necklace')
        parts.push(
            <mesh
                key="neck"
                geometry={geo('torus', 0.09, 0.012)}
                material={mat('#d4af37')}
                position={[0, TORSO - 0.03, 0.03]}
                rotation={[Math.PI / 2 - 0.5, 0, 0]}
            />,
        );
    return <>{parts}</>;
}

function FacialHair({ look }: { look: Look }) {
    switch (look.facialHair) {
        case 'stubble':
            return (
                <B
                    s={[0.29, 0.1, 0.012]}
                    p={[0, 0.06, 0.141]}
                    c={look.hair}
                    o={0.45}
                />
            );
        case 'beard':
            return (
                <>
                    <B s={[0.3, 0.1, 0.08]} p={[0, 0.03, 0.11]} c={look.hair} />
                    <B
                        s={[0.03, 0.12, 0.2]}
                        p={[-0.145, 0.1, 0.03]}
                        c={look.hair}
                    />
                    <B
                        s={[0.03, 0.12, 0.2]}
                        p={[0.145, 0.1, 0.03]}
                        c={look.hair}
                    />
                    <B
                        s={[0.12, 0.025, 0.012]}
                        p={[0, 0.098, 0.146]}
                        c={look.hair}
                    />
                </>
            );
        case 'moustache':
            return (
                <B s={[0.12, 0.028, 0.015]} p={[0, 0.1, 0.147]} c={look.hair} />
            );
        default:
            return null;
    }
}

function Hair({ look }: { look: Look }) {
    const h = look.hair;
    const cap = <B s={[0.32, 0.07, 0.3]} p={[0, 0.31, -0.01]} c={h} />;
    const back = (len: number) => (
        <B s={[0.32, len, 0.06]} p={[0, 0.3 - len / 2, -0.13]} c={h} />
    );
    const fringe = <B s={[0.3, 0.04, 0.03]} p={[0, 0.27, 0.135]} c={h} />;
    switch (look.hairStyle) {
        case 'bald':
            return null;
        case 'buzz':
            return (
                <B
                    s={[0.31, 0.03, 0.29]}
                    p={[0, 0.295, -0.005]}
                    c={shade(h, 0.1)}
                    o={0.85}
                />
            );
        case 'mohawk':
            return <B s={[0.06, 0.14, 0.3]} p={[0, 0.34, -0.01]} c={h} />;
        case 'curly':
            return (
                <>
                    {[
                        [-0.1, 0.3, 0.06],
                        [0.1, 0.31, 0.04],
                        [0, 0.33, -0.06],
                        [-0.14, 0.24, -0.1],
                        [0.14, 0.25, -0.1],
                        [0, 0.27, 0.1],
                        [-0.12, 0.2, -0.13],
                        [0.12, 0.2, -0.13],
                    ].map(([x, y, z]) => (
                        <mesh
                            key={`${x},${y},${z}`}
                            geometry={geo('ball', 0.09, 0)}
                            material={mat(h)}
                            position={[x, y, z]}
                            castShadow
                        />
                    ))}
                </>
            );
        case 'afro':
            return (
                <mesh
                    geometry={geo('ball', 0.24, 1)}
                    material={mat(h)}
                    position={[0, 0.3, -0.04]}
                    castShadow
                />
            );
        case 'spiky':
            return (
                <>
                    {cap}
                    {[
                        [-0.1, 0.06, -0.3],
                        [0, 0.02, 0],
                        [0.1, 0.06, 0.3],
                        [-0.06, -0.08, -0.15],
                        [0.06, -0.08, 0.15],
                    ].map(([x, z, tilt]) => (
                        <mesh
                            key={`${x},${z}`}
                            geometry={geo('cone', 0.05, 0.13, 4)}
                            material={mat(h)}
                            position={[x, 0.39, z]}
                            rotation={[z, 0, tilt]}
                            castShadow
                        />
                    ))}
                </>
            );
        case 'ponytail':
            return (
                <>
                    {cap}
                    {back(0.16)}
                    <mesh
                        geometry={geo('ball', 0.05, 0)}
                        material={mat(shade(h, -0.2))}
                        position={[0, 0.24, -0.17]}
                    />
                    <B
                        s={[0.08, 0.26, 0.07]}
                        p={[0, 0.1, -0.19]}
                        c={h}
                        r={[0.2, 0, 0]}
                    />
                </>
            );
        case 'braids':
            return (
                <>
                    {cap}
                    {back(0.14)}
                    {[-0.1, 0.1].map((x) => (
                        <group key={x}>
                            <B
                                s={[0.06, 0.34, 0.06]}
                                p={[x, 0.08, -0.13]}
                                c={h}
                            />
                            <B
                                s={[0.065, 0.025, 0.065]}
                                p={[x, -0.08, -0.13]}
                                c="#e63946"
                            />
                        </group>
                    ))}
                </>
            );
        case 'bob':
            return (
                <>
                    {cap}
                    {fringe}
                    {back(0.22)}
                    <B s={[0.04, 0.22, 0.28]} p={[-0.16, 0.19, -0.01]} c={h} />
                    <B s={[0.04, 0.22, 0.28]} p={[0.16, 0.19, -0.01]} c={h} />
                </>
            );
        case 'long':
            return (
                <>
                    {cap}
                    {back(0.44)}
                    <B s={[0.035, 0.3, 0.16]} p={[-0.16, 0.16, -0.07]} c={h} />
                    <B s={[0.035, 0.3, 0.16]} p={[0.16, 0.16, -0.07]} c={h} />
                </>
            );
        case 'bun':
            return (
                <>
                    {cap}
                    {back(0.2)}
                    <mesh
                        geometry={geo('ball', 0.08, 0)}
                        material={mat(h)}
                        position={[0, 0.38, -0.1]}
                        castShadow
                    />
                </>
            );
        default:
            return (
                <>
                    {cap}
                    {back(0.2)}
                    {fringe}
                </>
            );
    }
}

function Accessory({ look }: { look: Look }) {
    switch (look.accessory) {
        case 'glasses':
        case 'sunglasses': {
            const lens =
                look.accessory === 'sunglasses' ? '#111111' : '#cfe8ff';
            const o = look.accessory === 'sunglasses' ? 1 : 0.45;
            return (
                <group position={[0, 0.165, 0.152]}>
                    {[-0.07, 0.07].map((x) => (
                        <group key={x}>
                            <B
                                s={[0.1, 0.075, 0.01]}
                                p={[x, 0, 0]}
                                c="#1d1d1d"
                            />
                            <B
                                s={[0.08, 0.055, 0.012]}
                                p={[x, 0, 0.002]}
                                c={lens}
                                o={o}
                            />
                        </group>
                    ))}
                    <B s={[0.05, 0.012, 0.01]} p={[0, 0.01, 0]} c="#1d1d1d" />
                    <B
                        s={[0.01, 0.012, 0.16]}
                        p={[-0.152, 0.01, -0.08]}
                        c="#1d1d1d"
                    />
                    <B
                        s={[0.01, 0.012, 0.16]}
                        p={[0.152, 0.01, -0.08]}
                        c="#1d1d1d"
                    />
                </group>
            );
        }
        case 'hat':
            return (
                <group position={[0, 0.3, 0]}>
                    <mesh
                        geometry={geo('cyl', 0.24, 0.24, 0.025, 12)}
                        material={mat('#4a3b2a')}
                        castShadow
                    />
                    <mesh
                        geometry={geo('cyl', 0.13, 0.15, 0.16, 12)}
                        material={mat('#4a3b2a')}
                        position={[0, 0.08, 0]}
                        castShadow
                    />
                    <mesh
                        geometry={geo('cyl', 0.152, 0.152, 0.03, 12)}
                        material={mat('#c0392b')}
                        position={[0, 0.02, 0]}
                    />
                </group>
            );
        case 'cap':
            return (
                <group position={[0, 0.3, 0]}>
                    <B
                        s={[0.33, 0.08, 0.31]}
                        p={[0, 0.03, -0.01]}
                        c={look.top}
                    />
                    <B
                        s={[0.24, 0.02, 0.15]}
                        p={[0, -0.005, 0.2]}
                        c={shade(look.top, -0.2)}
                    />
                    <B
                        s={[0.04, 0.02, 0.04]}
                        p={[0, 0.08, -0.01]}
                        c={shade(look.top, -0.2)}
                    />
                </group>
            );
        case 'beanie':
            return (
                <group position={[0, 0.27, 0]}>
                    <mesh
                        geometry={geo('cyl', 0.15, 0.165, 0.13, 10)}
                        material={mat('#c0392b')}
                        position={[0, 0.06, -0.005]}
                        castShadow
                    />
                    <mesh
                        geometry={geo('cyl', 0.168, 0.168, 0.04, 10)}
                        material={mat('#962d22')}
                        position={[0, 0.0, -0.005]}
                    />
                    <mesh
                        geometry={geo('ball', 0.045, 0)}
                        material={mat('#f2f2f2')}
                        position={[0, 0.15, 0]}
                    />
                </group>
            );
        case 'bandana':
            return (
                <group>
                    <B
                        s={[0.32, 0.05, 0.3]}
                        p={[0, 0.25, -0.005]}
                        c="#c0392b"
                    />
                    <B
                        s={[0.04, 0.1, 0.03]}
                        p={[0.04, 0.2, -0.16]}
                        c="#c0392b"
                        r={[0, 0, 0.4]}
                    />
                </group>
            );
        case 'headphones':
            return (
                <group position={[0, 0.16, 0]}>
                    <mesh
                        geometry={geo('torus', 0.17, 0.02, Math.PI)}
                        material={mat('#333333')}
                        position={[0, 0.06, 0]}
                    />
                    {[-0.165, 0.165].map((x) => (
                        <B
                            key={x}
                            s={[0.05, 0.1, 0.1]}
                            p={[x, 0.02, 0]}
                            c="#333333"
                        />
                    ))}
                </group>
            );
        case 'earrings':
            return (
                <>
                    {[-0.155, 0.155].map((x) => (
                        <mesh
                            key={x}
                            geometry={geo('ball', 0.022, 0)}
                            material={mat('#d4af37')}
                            position={[x, 0.09, 0.02]}
                        />
                    ))}
                </>
            );
        case 'flower':
            return (
                <group position={[0.13, 0.27, 0.06]}>
                    {[0, 1, 2, 3, 4].map((i) => (
                        <mesh
                            key={i}
                            geometry={geo('ball', 0.03, 0)}
                            material={mat('#ff8fb8')}
                            position={[
                                Math.cos(i * 1.26) * 0.035,
                                Math.sin(i * 1.26) * 0.035,
                                0,
                            ]}
                        />
                    ))}
                    <mesh
                        geometry={geo('ball', 0.022, 0)}
                        material={mat('#ffd166')}
                        position={[0, 0, 0.015]}
                    />
                </group>
            );
        case 'bow':
            return (
                <group position={[0.08, 0.34, 0.02]}>
                    <B
                        s={[0.07, 0.06, 0.03]}
                        p={[-0.04, 0, 0]}
                        c="#e63946"
                        r={[0, 0, 0.3]}
                    />
                    <B
                        s={[0.07, 0.06, 0.03]}
                        p={[0.04, 0, 0]}
                        c="#e63946"
                        r={[0, 0, -0.3]}
                    />
                    <B s={[0.03, 0.03, 0.035]} p={[0, 0, 0]} c="#b5202c" />
                </group>
            );
        default:
            return null;
    }
}
