import { useFrame } from '@react-three/fiber';
import { useEffect, useMemo, useRef } from 'react';
import * as THREE from 'three';
import { SCENE_DETAIL } from '../figure/compile';
import { figureDoc } from '../figure/dress';
import { PAIRED, SEATED } from '../figure/moves';
import { setInk, warmupMeshes } from '../figure/object';
import { Puppet } from '../figure/puppet';
import { type Quality, useFigure } from '../figure/runtime';
import type { Look } from '../iso/appearance';
import type { Pt } from '../iso/iso';
import type { Pose } from './motion';

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

export const SCENE_QUALITY: Quality = {
    ...SCENE_DETAIL,
    cloth: 512,
    face: 256,
};

/** Ink line width in drawing-buffer pixels. */
const INK = 1.1;
/** Seconds a walk cycle is sped up by: steps are quick in a small town. */
const WALK_SPEED = 2.1;
const BLINK_EVERY = 4.2;
/** Sit toward the front of a seat, so long legs clear it. */
const SEAT_EDGE = 0.2;

const buffer = new THREE.Vector2();

const turnTo = (from: number, to: number, k: number) => {
    let d = to - from;
    while (d > Math.PI) d -= Math.PI * 2;
    while (d < -Math.PI) d += Math.PI * 2;
    return from + d * k;
};

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
const phoneGeo = new THREE.BoxGeometry(0.028, 0.062, 0.008);
const phoneMat = new THREE.MeshBasicMaterial({ color: '#9cc7ff' });

/** One of each kind of material an avatar draws with, to compile before anyone walks in. */
export const avatarMaterials = (): THREE.Material[] => [
    meRingMat,
    blobMat,
    phoneMat,
];

/** Compiles the skinned Figure shaders up front, too small to see. */
export function AvatarWarmup() {
    const group = useMemo(() => warmupMeshes(), []);
    return <primitive object={group} scale={0.0001} />;
}

/** What the Body is doing this frame, as a clip or move name. */
function animation(
    s: AvatarState,
    named: string | null,
    seated: boolean,
    side: number,
) {
    const { pose } = s;
    if (pose.moving) return 'walk';
    if (s.asleep) return 'sleep';
    if (s.lying || named === 'lie') return 'lie';
    if (named === 'hold-hands')
        return side > 0 ? 'hold-hands-r' : 'hold-hands-l';
    if (named) return named;
    if (seated) return s.phone ? 'sit-phone' : s.talking ? 'sit-talk' : 'sit';
    if (s.phone) return 'phone';
    return s.talking ? 'talk' : 'stand';
}

/** A person: a compiled Figure, with clips and moves picked from the Body's state. */
export default function Avatar({
    look,
    isMe,
    state,
    onClick,
    children,
}: Props) {
    const root = useRef<THREE.Group>(null);
    const doc = useMemo(() => figureDoc(look), [look]);
    const figure = useFigure(doc, SCENE_QUALITY);
    const puppet = useRef<Puppet | null>(null);
    const facing = useRef(0);
    const height = useRef(0);
    const phone = useRef<THREE.Mesh | null>(null);
    const seed = useMemo(() => Math.random() * 10, []);

    useEffect(() => {
        if (!figure) return;
        const p = new Puppet(figure, doc);
        puppet.current = p;
        const hand = figure.byName.get('hand_r');
        const held = new THREE.Mesh(phoneGeo, phoneMat);
        held.position.set(0.01, 0.07, 0.03);
        held.visible = false;
        hand?.add(held);
        phone.current = held;
        return () => {
            p.dispose();
            hand?.remove(held);
            puppet.current = null;
        };
    }, [figure, doc]);

    useFrame(({ clock, gl }, dt) => {
        const s = state();
        const g = root.current;
        if (!g) return;
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
            (seat !== undefined || (named !== null && SEATED.has(named)));

        // Facing: the way you walk, your partner, or your slot.
        let target = facing.current;
        const partner = named && PAIRED.has(named) ? s.partner : null;
        let side = 1;
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
            side = Math.cos(target) * dx - Math.sin(target) * dz;
        } else if (pose.slot?.face !== undefined)
            target = (pose.slot.face * Math.PI) / 2;
        facing.current = turnTo(facing.current, target, moving ? 0.35 : 0.12);
        const f = facing.current;

        const p = puppet.current;
        const name = animation(s, named, sitting && seat !== undefined, side);
        p?.play(name, moving ? 0.14 : 0.3, moving ? WALK_SPEED : 1);
        p?.update(dt);

        // Where the feet go.
        let x = pose.tile.x + 0.5;
        let z = pose.tile.y + 0.5;
        let y = s.ground + (figure?.lift ?? 0);
        if (sitting && p) {
            const hips = seat ?? s.ground + (named === 'perch' ? 0.5 : 0.1);
            y = hips - p.hips(name) + 0.06;
            // The sit clip leans the hips back off the feet, so bring them over the seat.
            const forward =
                p.reach(name) + (seat === undefined ? 0 : SEAT_EDGE);
            x += Math.sin(f) * forward;
            z += Math.cos(f) * forward;
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
        // Stairs and seats ease; a jump (arriving, door) doesn't.
        if (moving || Math.abs(height.current - y) > 1) height.current = y;
        else
            height.current +=
                (y - height.current) * (1 - Math.exp(-Math.min(dt, 0.1) * 8));

        g.position.set(x, height.current, z);
        g.rotation.set(0, f, 0);
        if (figure) figure.group.rotation.x = lying ? -Math.PI / 2 : 0;
        if (phone.current) phone.current.visible = s.phone && !lying && !named;

        // Blink every few seconds; talk with the mouth; smile when dancing.
        const blinking = (t / BLINK_EVERY) % 1 < 0.035 || s.asleep;
        const speaking = s.talking && !s.asleep;
        const col = blinking
            ? 1
            : speaking
              ? Math.sin(t * 14) > 0.2
                  ? 3
                  : Math.sin(t * 9) > 0
                    ? 2
                    : 0
              : 0;
        figure?.face(
            col,
            named === 'dance' ||
                named === 'hug' ||
                named === 'slow-dance' ||
                named === 'high-five',
        );

        gl.getDrawingBufferSize(buffer);
        setInk(INK, buffer.x, buffer.y);
    });

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
            {figure ? <primitive object={figure.group} /> : null}
            {children}
        </group>
    );
}
