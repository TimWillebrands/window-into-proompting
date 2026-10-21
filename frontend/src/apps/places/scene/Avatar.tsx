import { useFrame } from '@react-three/fiber';
import { useMemo, useRef } from 'react';
import type * as THREE from 'three';
import type { Look } from '../iso/appearance';
import type { Pose } from './motion';

const S = 1.1;
const HIP = 0.34;
const SHOULDER = 0.66;

export interface AvatarState {
    pose: Pose;
    /** World height under the feet. */
    ground: number;
    asleep: boolean;
    lying: boolean;
    phone: boolean;
    talking: boolean;
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

/** A blocky little person in their own colours. Walks, sits, lies down, holds a phone. */
export default function Avatar({
    look,
    isMe,
    state,
    onClick,
    children,
}: Props) {
    const root = useRef<THREE.Group>(null);
    const body = useRef<THREE.Group>(null);
    const legL = useRef<THREE.Group>(null);
    const legR = useRef<THREE.Group>(null);
    const armL = useRef<THREE.Group>(null);
    const armR = useRef<THREE.Group>(null);
    const head = useRef<THREE.Group>(null);
    const phone = useRef<THREE.Mesh>(null);
    const facing = useRef(0);
    const seed = useMemo(() => Math.random() * 10, []);

    useFrame(({ clock }) => {
        const s = state();
        const g = root.current;
        if (!g || !body.current) return;
        g.visible = !!s;
        if (!s) return;
        const t = clock.elapsedTime + seed;
        const { pose } = s;
        const sitting = !pose.moving && !!pose.slot?.seat && !s.lying;
        const lying = !pose.moving && s.lying;
        let target = facing.current;
        if (pose.moving && pose.toward) {
            const dx = pose.toward.x - pose.tile.x;
            const dz = pose.toward.y - pose.tile.y;
            if (Math.abs(dx) + Math.abs(dz) > 0.001)
                target = Math.atan2(dx, dz);
        } else if (pose.slot?.face !== undefined)
            target = (pose.slot.face * Math.PI) / 2;
        facing.current = turnTo(
            facing.current,
            target,
            pose.moving ? 0.35 : 0.15,
        );
        const seatH = pose.slot?.h ?? 0;
        let y = s.ground;
        let x = pose.tile.x + 0.5;
        let z = pose.tile.y + 0.5;
        if (sitting) y = seatH - HIP * S + 0.04;
        if (lying) {
            y = seatH + 0.12;
            x += Math.sin(facing.current) * 0.9;
            z += Math.cos(facing.current) * 0.9;
        }
        g.position.set(x, y, z);
        g.rotation.set(0, facing.current, 0);
        body.current.rotation.x = lying ? -Math.PI / 2 : 0;
        const swing = pose.moving ? Math.sin(t * 13) * 0.6 : 0;
        body.current.position.y = pose.moving
            ? Math.abs(Math.sin(t * 13)) * 0.04
            : Math.sin(t * 1.6) * 0.005;
        if (legL.current && legR.current) {
            legL.current.rotation.x = sitting ? -Math.PI / 2 : swing;
            legR.current.rotation.x = sitting ? -Math.PI / 2 : -swing;
        }
        if (armL.current && armR.current) {
            armL.current.rotation.x = -swing * 0.8;
            armR.current.rotation.x = s.phone ? -1.1 : swing * 0.8;
            armR.current.rotation.z = s.phone ? 0.25 : 0;
        }
        if (phone.current) phone.current.visible = s.phone && !lying;
        if (head.current) {
            head.current.rotation.x = s.talking
                ? Math.sin(t * 9) * 0.08
                : s.phone
                  ? 0.35
                  : 0;
            head.current.rotation.y =
                !pose.moving && !s.phone && !lying
                    ? Math.sin(t * 0.4) * 0.25
                    : 0;
        }
    });

    const box = (
        w: number,
        h: number,
        d: number,
        c: string,
        y = 0,
        x = 0,
        z = 0,
    ) => (
        <mesh position={[x, y, z]} castShadow>
            <boxGeometry args={[w, h, d]} />
            <meshStandardMaterial color={c} flatShading roughness={0.85} />
        </mesh>
    );

    return (
        // biome-ignore lint/a11y/noStaticElementInteractions: a three.js group, not DOM
        <group
            ref={root}
            onClick={(e) => {
                e.stopPropagation();
                onClick?.();
            }}
        >
            {isMe ? (
                <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.02, 0]}>
                    <ringGeometry args={[0.32, 0.4, 24]} />
                    <meshBasicMaterial
                        color="#3b82f6"
                        transparent
                        opacity={0.8}
                    />
                </mesh>
            ) : null}
            <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.01, 0]}>
                <circleGeometry args={[0.24, 16]} />
                <meshBasicMaterial
                    color="#000000"
                    transparent
                    opacity={0.18}
                    depthWrite={false}
                />
            </mesh>
            <group scale={S}>
                <group ref={body}>
                    <group ref={legL} position={[-0.075, HIP, 0]}>
                        {box(
                            0.13,
                            HIP - 0.04,
                            0.14,
                            look.bottom,
                            -(HIP - 0.04) / 2,
                        )}
                        {box(0.14, 0.06, 0.18, '#2b2b2b', -HIP + 0.03, 0, 0.02)}
                    </group>
                    <group ref={legR} position={[0.075, HIP, 0]}>
                        {box(
                            0.13,
                            HIP - 0.04,
                            0.14,
                            look.bottom,
                            -(HIP - 0.04) / 2,
                        )}
                        {box(0.14, 0.06, 0.18, '#2b2b2b', -HIP + 0.03, 0, 0.02)}
                    </group>
                    {box(0.34, 0.36, 0.2, look.top, HIP + 0.17)}
                    {look.accessory === 'scarf'
                        ? box(0.36, 0.08, 0.24, '#e8a33c', SHOULDER + 0.02)
                        : null}
                    <group ref={armL} position={[-0.225, SHOULDER, 0]}>
                        {box(0.1, 0.3, 0.12, look.top, -0.14)}
                        {box(0.09, 0.07, 0.09, look.skin, -0.32)}
                    </group>
                    <group ref={armR} position={[0.225, SHOULDER, 0]}>
                        {box(0.1, 0.3, 0.12, look.top, -0.14)}
                        {box(0.09, 0.07, 0.09, look.skin, -0.32)}
                        <mesh
                            ref={phone}
                            position={[0, -0.36, 0.05]}
                            rotation={[0.3, 0, 0]}
                        >
                            <boxGeometry args={[0.07, 0.12, 0.02]} />
                            <meshBasicMaterial color="#9cc7ff" />
                        </mesh>
                    </group>
                    <group ref={head} position={[0, HIP + 0.36, 0]}>
                        {box(0.3, 0.28, 0.28, look.skin, 0.15)}
                        {box(0.045, 0.05, 0.02, '#1d1d1d', 0.16, -0.07, 0.14)}
                        {box(0.045, 0.05, 0.02, '#1d1d1d', 0.16, 0.07, 0.14)}
                        <Hair look={look} />
                        <Accessory look={look} />
                    </group>
                </group>
            </group>
            {children}
        </group>
    );
}

function Hair({ look }: { look: Look }) {
    const m = (
        <meshStandardMaterial color={look.hair} flatShading roughness={0.9} />
    );
    switch (look.hairStyle) {
        case 'bald':
            return null;
        case 'mohawk':
            return (
                <mesh position={[0, 0.34, -0.01]} castShadow>
                    <boxGeometry args={[0.06, 0.14, 0.3]} />
                    {m}
                </mesh>
            );
        case 'curly':
            return (
                <group>
                    {[
                        [-0.1, 0.3, 0.06],
                        [0.1, 0.31, 0.04],
                        [0, 0.33, -0.06],
                        [-0.14, 0.24, -0.1],
                        [0.14, 0.25, -0.1],
                        [0, 0.27, 0.1],
                    ].map(([x, y, z]) => (
                        <mesh
                            key={`${x},${y},${z}`}
                            position={[x, y, z]}
                            castShadow
                        >
                            <icosahedronGeometry args={[0.09, 0]} />
                            {m}
                        </mesh>
                    ))}
                </group>
            );
        default:
            return (
                <group>
                    <mesh position={[0, 0.31, -0.01]} castShadow>
                        <boxGeometry args={[0.32, 0.07, 0.3]} />
                        {m}
                    </mesh>
                    <mesh
                        position={[
                            0,
                            look.hairStyle === 'long' ? 0.08 : 0.2,
                            -0.13,
                        ]}
                        castShadow
                    >
                        <boxGeometry
                            args={[
                                0.32,
                                look.hairStyle === 'long' ? 0.42 : 0.2,
                                0.06,
                            ]}
                        />
                        {m}
                    </mesh>
                    {look.hairStyle === 'bun' ? (
                        <mesh position={[0, 0.38, -0.1]} castShadow>
                            <icosahedronGeometry args={[0.08, 0]} />
                            {m}
                        </mesh>
                    ) : null}
                </group>
            );
    }
}

function Accessory({ look }: { look: Look }) {
    switch (look.accessory) {
        case 'glasses':
            return (
                <group position={[0, 0.17, 0.145]}>
                    {[-0.07, 0.07].map((x) => (
                        <mesh key={x} position={[x, 0, 0]}>
                            <boxGeometry args={[0.09, 0.07, 0.01]} />
                            <meshBasicMaterial color="#1d1d1d" wireframe />
                        </mesh>
                    ))}
                    <mesh>
                        <boxGeometry args={[0.3, 0.015, 0.01]} />
                        <meshBasicMaterial color="#1d1d1d" />
                    </mesh>
                </group>
            );
        case 'hat':
            return (
                <group position={[0, 0.3, 0]}>
                    <mesh castShadow>
                        <cylinderGeometry args={[0.24, 0.24, 0.025, 12]} />
                        <meshStandardMaterial color="#4a3b2a" flatShading />
                    </mesh>
                    <mesh position={[0, 0.08, 0]} castShadow>
                        <cylinderGeometry args={[0.13, 0.15, 0.16, 12]} />
                        <meshStandardMaterial color="#4a3b2a" flatShading />
                    </mesh>
                </group>
            );
        case 'beanie':
            return (
                <mesh position={[0, 0.3, 0]} castShadow>
                    <sphereGeometry
                        args={[0.17, 10, 6, 0, Math.PI * 2, 0, Math.PI / 2]}
                    />
                    <meshStandardMaterial color="#c0392b" flatShading />
                </mesh>
            );
        case 'headphones':
            return (
                <group position={[0, 0.16, 0]}>
                    <mesh position={[0, 0.06, 0]}>
                        <torusGeometry args={[0.17, 0.02, 6, 16, Math.PI]} />
                        <meshStandardMaterial color="#333" />
                    </mesh>
                    {[-0.165, 0.165].map((x) => (
                        <mesh key={x} position={[x, 0.02, 0]}>
                            <boxGeometry args={[0.05, 0.1, 0.1]} />
                            <meshStandardMaterial color="#333" />
                        </mesh>
                    ))}
                </group>
            );
        default:
            return null;
    }
}
