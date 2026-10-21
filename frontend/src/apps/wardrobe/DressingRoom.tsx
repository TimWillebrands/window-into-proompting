import { Canvas, useFrame, useThree } from '@react-three/fiber';
import { useEffect, useMemo, useRef } from 'react';
import type * as THREE from 'three';
import type { Appearance } from '../../api/model/appearance';
import { lookOf } from '../places/iso/appearance';
import Avatar, { type AvatarState } from '../places/scene/Avatar';

interface Props {
    appearance: Appearance | null | undefined;
    fallbackColour: string;
    /** A little dance after a purchase, a wave on arrival. */
    mood: 'idle' | 'dance' | 'wave';
}

/** The fitting room: your figure on a slowly turning podium; drag to spin it. */
export default function DressingRoom({
    appearance,
    fallbackColour,
    mood,
}: Props) {
    const spin = useRef({
        angle: 0.35,
        velocity: 0,
        dragging: false,
        lastX: 0,
    });
    const look = useMemo(
        () => lookOf(appearance, fallbackColour),
        [appearance, fallbackColour],
    );
    return (
        <div
            className="wr-stage"
            onPointerDown={(e) => {
                spin.current.dragging = true;
                spin.current.lastX = e.clientX;
                e.currentTarget.setPointerCapture(e.pointerId);
            }}
            onPointerMove={(e) => {
                const s = spin.current;
                if (!s.dragging) return;
                const dx = e.clientX - s.lastX;
                s.lastX = e.clientX;
                s.angle += dx * 0.012;
                s.velocity = dx * 0.012;
            }}
            onPointerUp={() => {
                spin.current.dragging = false;
            }}
        >
            <Canvas
                shadows="percentage"
                flat
                dpr={[1, 2]}
                camera={{ position: [0, 1.2, 3.6], fov: 30 }}
                gl={{ alpha: true }}
            >
                <ambientLight intensity={0.9} color="#fff1f6" />
                <spotLight
                    position={[1.5, 4, 3]}
                    angle={0.5}
                    penumbra={0.6}
                    intensity={40}
                    castShadow
                    color="#fff6e8"
                />
                <pointLight
                    position={[-2, 1.5, -1.5]}
                    intensity={6}
                    color="#b18cff"
                />
                <pointLight
                    position={[2, 0.6, -1]}
                    intensity={4}
                    color="#ff8fc8"
                />
                <Aim y={0.72} />
                <Podium spin={spin} look={look} mood={mood} />
            </Canvas>
            <span className="wr-stage-hint">⟲ drag to turn</span>
        </div>
    );
}

/** Look at the figure's middle, not the floor. */
function Aim({ y }: { y: number }) {
    const camera = useThree((s) => s.camera);
    useEffect(() => {
        camera.lookAt(0, y, 0);
    }, [camera, y]);
    return null;
}

function Podium({
    spin,
    look,
    mood,
}: {
    spin: React.RefObject<{
        angle: number;
        velocity: number;
        dragging: boolean;
        lastX: number;
    }>;
    look: ReturnType<typeof lookOf>;
    mood: Props['mood'];
}) {
    const turn = useRef<THREE.Group>(null);
    const sparkles = useRef<THREE.Group>(null);
    useFrame(({ clock }, dt) => {
        const s = spin.current;
        if (!s) return;
        if (!s.dragging) {
            s.velocity *= 0.92;
            s.angle +=
                s.velocity +
                (Math.abs(s.velocity) < 0.002
                    ? Math.sin(clock.elapsedTime * 0.5) * 0.0025
                    : 0);
        }
        if (turn.current) turn.current.rotation.y = s.angle;
        if (sparkles.current) {
            sparkles.current.rotation.y += dt * 1.5;
            sparkles.current.visible = mood === 'dance';
        }
    });
    const state = useMemo(
        () => (): AvatarState => ({
            pose: {
                tile: { x: -0.5, y: -0.5 },
                moving: false,
                slot: { tile: { x: -0.5, y: -0.5 }, face: 0 },
            },
            ground: 0.12,
            asleep: false,
            lying: false,
            phone: false,
            talking: false,
            hangout: mood === 'idle' ? null : mood,
            partner: null,
        }),
        [mood],
    );
    return (
        <group position={[0, -0.05, 0]}>
            <mesh receiveShadow position={[0, 0.06, 0]}>
                <cylinderGeometry args={[0.85, 0.92, 0.12, 40]} />
                <meshStandardMaterial color="#f5e6ff" roughness={0.4} />
            </mesh>
            <mesh position={[0, 0.125, 0]} rotation={[Math.PI / 2, 0, 0]}>
                <torusGeometry args={[0.86, 0.022, 8, 48]} />
                <meshStandardMaterial
                    color="#e2b84a"
                    metalness={0.6}
                    roughness={0.3}
                />
            </mesh>
            <mesh
                rotation={[-Math.PI / 2, 0, 0]}
                position={[0, 0.001, 0]}
                receiveShadow
            >
                <circleGeometry args={[2.2, 40]} />
                <meshStandardMaterial
                    color="#3b2552"
                    roughness={0.9}
                    transparent
                    opacity={0.35}
                />
            </mesh>
            <group ref={turn}>
                <Avatar look={look} isMe={false} state={state} />
            </group>
            <group ref={sparkles}>
                {[0, 1, 2, 3, 4, 5].map((i) => (
                    <mesh
                        key={i}
                        position={[
                            Math.cos((i / 6) * Math.PI * 2) * 0.7,
                            0.5 + (i % 3) * 0.35,
                            Math.sin((i / 6) * Math.PI * 2) * 0.7,
                        ]}
                    >
                        <octahedronGeometry args={[0.05, 0]} />
                        <meshBasicMaterial
                            color={i % 2 ? '#ffe17a' : '#ff9ad5'}
                        />
                    </mesh>
                ))}
            </group>
        </group>
    );
}
