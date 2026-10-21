import { Html, MapControls, OrthographicCamera } from '@react-three/drei';
import { Canvas, type ThreeEvent, useFrame } from '@react-three/fiber';
import { useEffect, useMemo, useRef, useState } from 'react';
import * as THREE from 'three';
import { useChatGroupMessages } from '#lib/realtime-store';
import type { Body } from '../../../api/model/body';
import type { FurniView } from '../../../api/model/furniView';
import type { Place } from '../../../api/model/place';
import { lookOf } from '../iso/appearance';
import type { Pt } from '../iso/iso';
import { avatarColour, canHear, scribble, volumeOf } from '../places-lib';
import Avatar, { type AvatarState } from './Avatar';
import { type Built, build, type Placement } from './build';
import { buildGround } from './ground';
import { poseOf, round, targets, type Walk } from './motion';
import {
    type PlacedProp,
    pathIn,
    type Scene,
    spotNear,
    standHeight,
    withFurni,
} from './scene';
import { placeFurni, sceneFor } from './scenes';
import { type Sky, skyAt } from './sky';

const BUBBLE_MS = 14_000;
const VIEW = new THREE.Vector3(0.612, 0.5, 0.612).normalize();

interface Props {
    place: Place;
    places: Place[];
    bodies: Body[];
    furni: FurniView[];
    playerId: string | null;
    allEars?: boolean;
    onBodyClick?: (participantId: string) => void;
    onSpotClick?: (spotId: string) => void;
    /** Pin the hour (previews); defaults to the local clock. */
    hour?: number;
    /** Arranging a Home: click a thing to pick it, then a tile to put it there. */
    arrange?: {
        selected: string | null;
        onPick: (furniId: string) => void;
        onTile: (tile: Pt) => void;
    };
}

const PIXEL_KEY = 'places.pixel';

/** The 3D view of one Place (ADR 0023): scene from its map and prefabs, Bodies walking about, talk in bubbles. */
export default function PlaceScene(props: Props) {
    const { place, furni } = props;
    const scene = useMemo(() => sceneFor(place), [place]);
    const wrap = useRef<HTMLDivElement>(null);
    const [width, setWidth] = useState(640);
    const [pixel, setPixel] = useState(
        () => localStorage.getItem(PIXEL_KEY) !== 'off',
    );
    const [zoomBy, setZoomBy] = useState(1);
    const [recentre, setRecentre] = useState(0);
    const hour = useHour(props.hour);
    const sky = skyAt(hour, !!scene.def.outdoor);
    useEffect(() => {
        const el = wrap.current;
        if (!el) return;
        const ro = new ResizeObserver(() => setWidth(el.clientWidth));
        ro.observe(el);
        setWidth(el.clientWidth);
        return () => ro.disconnect();
    }, []);
    const height = Math.round(Math.min(560, Math.max(300, width * 0.62)));
    const here = furni.filter((f) => f.placeId === place.id);

    return (
        <div
            ref={wrap}
            style={{
                position: 'relative',
                width: '100%',
                height,
                borderRadius: 2,
                overflow: 'hidden',
                background: sky.background,
            }}
        >
            <Canvas
                shadows
                flat
                dpr={pixel ? 0.5 : [1, 2]}
                gl={{ antialias: !pixel }}
                style={{
                    imageRendering: pixel ? 'pixelated' : 'auto',
                    cursor: props.onSpotClick ? 'pointer' : 'default',
                }}
            >
                <color attach="background" args={[sky.background]} />
                <Rig
                    scene={scene}
                    width={width}
                    height={height}
                    zoomBy={zoomBy}
                    recentre={recentre}
                    focus={focusOf(scene, props)}
                />
                <Lights scene={scene} sky={sky} />
                <World {...props} scene={scene} sky={sky} furni={here} />
            </Canvas>
            <div
                style={{
                    position: 'absolute',
                    top: 4,
                    right: 4,
                    display: 'flex',
                    gap: 2,
                }}
            >
                <button
                    type="button"
                    title="Zoom in"
                    onClick={() => setZoomBy((z) => Math.min(2.5, z * 1.25))}
                >
                    +
                </button>
                <button
                    type="button"
                    title="Zoom out"
                    onClick={() => setZoomBy((z) => Math.max(0.4, z / 1.25))}
                >
                    −
                </button>
                <button
                    type="button"
                    title="Back to you"
                    onClick={() => setRecentre((n) => n + 1)}
                >
                    ⌖
                </button>
                <button
                    type="button"
                    title="Pixel look on/off"
                    onClick={() =>
                        setPixel((p) => {
                            localStorage.setItem(PIXEL_KEY, p ? 'off' : 'on');
                            return !p;
                        })
                    }
                >
                    {pixel ? '▦' : '▢'}
                </button>
            </div>
        </div>
    );
}

const localHour = () => {
    const d = new Date();
    return d.getHours() + d.getMinutes() / 60;
};

function useHour(pinned?: number) {
    const [hour, setHour] = useState(localHour);
    useEffect(() => {
        if (pinned !== undefined) return;
        const t = setInterval(() => setHour(localHour()), 60_000);
        return () => clearInterval(t);
    }, [pinned]);
    return pinned ?? hour;
}

function useNow(ms: number) {
    const [now, setNow] = useState(Date.now());
    useEffect(() => {
        const t = setInterval(() => setNow(Date.now()), ms);
        return () => clearInterval(t);
    }, [ms]);
    return now;
}

/** The camera starts on you if you're here, else the scene's focus, else its middle. */
function focusOf(scene: Scene, { bodies, playerId, place }: Props): Pt {
    const me = bodies.find(
        (b) =>
            b.participantId === playerId &&
            (b.destPlaceId ?? b.placeId) === place.id,
    );
    const spot = me ? scene.spots[me.destSpotId ?? me.spotId ?? ''] : undefined;
    if (spot) return spot.at;
    const f = scene.def.focus;
    return f ? { x: f[0], y: f[1] } : { x: scene.w / 2, y: scene.h / 2 };
}

/** Habbo's angle: 2:1 dimetric, looking from +x, +z. Rooms fit the view; outdoors stays close and pans. */
function Rig({
    scene,
    width,
    height,
    zoomBy,
    recentre,
    focus,
}: {
    scene: Scene;
    width: number;
    height: number;
    zoomBy: number;
    recentre: number;
    focus: Pt;
}) {
    const cam = useRef<THREE.OrthographicCamera>(null);
    // Controls must drive this camera, which only exists after the first commit.
    const [camera, setCamera] = useState<THREE.OrthographicCamera | null>(null);
    useEffect(() => setCamera(cam.current), []);
    const controls = useRef<React.ComponentRef<typeof MapControls>>(null);
    const fx = focus.x;
    const fy = focus.y;
    // biome-ignore lint/correctness/useExhaustiveDependencies: recentre is a trigger
    useEffect(() => {
        const c = cam.current;
        if (!c) return;
        let target = new THREE.Vector3(scene.w / 2, 0.6, scene.h / 2);
        let zoom: number;
        c.position.copy(target).addScaledVector(VIEW, 60);
        c.lookAt(target);
        c.updateMatrixWorld();
        if (scene.def.outdoor) {
            target = new THREE.Vector3(fx + 0.5, 0.4, fy + 0.5);
            zoom = Math.min(48, Math.max(28, width / 20));
        } else {
            const pts: THREE.Vector3[] = [];
            for (const x of [0, scene.w])
                for (const z of [0, scene.h])
                    for (const y of [-0.3, 2.5])
                        pts.push(
                            new THREE.Vector3(x, y, z).applyMatrix4(
                                c.matrixWorldInverse,
                            ),
                        );
            const xs = pts.map((p) => p.x);
            const ys = pts.map((p) => p.y);
            const [x0, x1, y0, y1] = [
                Math.min(...xs),
                Math.max(...xs),
                Math.min(...ys),
                Math.max(...ys),
            ];
            zoom = Math.min(width / (x1 - x0), height / (y1 - y0)) * 0.94;
            const right = new THREE.Vector3().setFromMatrixColumn(
                c.matrixWorld,
                0,
            );
            const up = new THREE.Vector3().setFromMatrixColumn(
                c.matrixWorld,
                1,
            );
            target
                .addScaledVector(right, (x0 + x1) / 2)
                .addScaledVector(up, (y0 + y1) / 2);
        }
        c.zoom = zoom * zoomBy;
        c.position.copy(target).addScaledVector(VIEW, 60);
        c.lookAt(target);
        c.updateProjectionMatrix();
        const ctl = controls.current;
        if (ctl) {
            ctl.target.copy(target);
            ctl.update();
        }
    }, [scene, width, height, zoomBy, recentre, fx, fy, camera]);
    return (
        <>
            <OrthographicCamera ref={cam} makeDefault near={-100} far={300} />
            {camera ? (
                <MapControls
                    ref={controls}
                    camera={camera}
                    enableRotate={false}
                    enableZoom={false}
                    screenSpacePanning
                    enablePan={!!scene.def.outdoor || zoomBy > 1}
                />
            ) : null}
        </>
    );
}

function Lights({ scene, sky }: { scene: Scene; sky: Sky }) {
    const sun = useRef<THREE.DirectionalLight>(null);
    const span = Math.max(scene.w, scene.h);
    useEffect(() => {
        const l = sun.current;
        if (!l) return;
        l.target.position.set(scene.w / 2, 0, scene.h / 2);
        l.target.updateMatrixWorld();
        const sc = l.shadow.camera;
        sc.left = -span;
        sc.right = span;
        sc.top = span;
        sc.bottom = -span;
        sc.updateProjectionMatrix();
    }, [scene, span]);
    return (
        <>
            <ambientLight
                intensity={sky.ambient}
                color={scene.def.outdoor ? '#dfe8ff' : '#fff1de'}
            />
            <hemisphereLight args={['#cfe3ff', '#7a6a55', 0.35]} />
            <directionalLight
                ref={sun}
                castShadow
                color={sky.sun}
                intensity={sky.sunPower}
                position={[
                    scene.w / 2 + sky.sunAt[0],
                    sky.sunAt[1],
                    scene.h / 2 + sky.sunAt[2],
                ]}
                shadow-mapSize={[2048, 2048]}
                shadow-bias={-0.0008}
                shadow-normalBias={0.02}
            />
        </>
    );
}

const plainMat = new THREE.MeshStandardMaterial({
    vertexColors: true,
    flatShading: true,
    roughness: 0.85,
});
const glowMat = new THREE.MeshBasicMaterial({ vertexColors: true });

function World(props: Props & { scene: Scene; sky: Sky }) {
    const {
        scene,
        sky,
        furni,
        place,
        places,
        bodies,
        playerId,
        onSpotClick,
        onBodyClick,
    } = props;
    const ground = useMemo(() => buildGround(scene), [scene]);
    const statics = useMemo(
        () =>
            build([
                ...ground.houses,
                ...scene.props
                    .filter((p) => !p.furniId)
                    .map((p) => placement(scene, p)),
            ]),
        [scene, ground],
    );
    const furniKey = JSON.stringify(
        furni.map((f) => [
            f.furniId,
            f.condition,
            f.on,
            f.notable,
            f.style,
            f.model?.length,
            f.tileX,
            f.tileZ,
            f.rot,
        ]),
    );
    // biome-ignore lint/correctness/useExhaustiveDependencies: furniKey covers what changes the look
    const furniProps = useMemo(() => {
        const byId = new Map(furni.map((f) => [f.furniId ?? '', f]));
        return placeFurni(scene, furni).map((p) => ({
            prop: p,
            view: byId.get(p.furniId ?? ''),
        }));
    }, [scene, furniKey]);
    // One build per thing, so each can be picked when arranging.
    const furniBuilt = useMemo(
        () =>
            furniProps.map(({ prop, view }) => ({
                id: prop.furniId ?? '',
                prop,
                built: build([
                    placement(
                        scene,
                        prop,
                        view && {
                            condition: Number(view.condition ?? 1),
                            on: !!view.on,
                            notable: !!view.notable,
                        },
                    ),
                ]),
            })),
        [furniProps, scene],
    );
    const { arrange } = props;
    // Things where they stand now block walking and standing room.
    const walkable = useMemo(
        () =>
            withFurni(
                scene,
                furniProps.map((f) => f.prop),
            ),
        [scene, furniProps],
    );
    const [hover, setHover] = useState<Pt | null>(null);
    const tileOf = (e: ThreeEvent<PointerEvent | MouseEvent>): Pt => ({
        x: Math.floor(e.point.x),
        y: Math.floor(e.point.z),
    });

    return (
        <>
            {/* biome-ignore lint/a11y/noStaticElementInteractions: a three.js group, not DOM */}
            <group
                onPointerMove={(e) => {
                    e.stopPropagation();
                    setHover(tileOf(e));
                }}
                onPointerOut={() => setHover(null)}
                onClick={(e) => {
                    if (e.delta > 6) return;
                    if (arrange) {
                        arrange.onTile(tileOf(e));
                        return;
                    }
                    if (!onSpotClick) return;
                    const spot = spotNear(scene, tileOf(e));
                    if (spot) onSpotClick(spot);
                }}
            >
                {ground.solid ? (
                    <mesh
                        geometry={ground.solid}
                        material={plainMat}
                        receiveShadow
                        castShadow
                    />
                ) : null}
                {ground.glass ? (
                    <mesh geometry={ground.glass}>
                        <meshBasicMaterial color={sky.pane} />
                    </mesh>
                ) : null}
                {ground.lights && sky.night ? (
                    <mesh geometry={ground.lights} material={glowMat} />
                ) : null}
                <BuiltMeshes built={statics} night={sky.night} />
                {furniBuilt.map((f) => (
                    // biome-ignore lint/a11y/noStaticElementInteractions: a three.js group, not DOM
                    <group
                        key={f.id}
                        onClick={
                            arrange
                                ? (e) => {
                                      if (e.delta > 6 || !f.id) return;
                                      e.stopPropagation();
                                      arrange.onPick(f.id);
                                  }
                                : undefined
                        }
                    >
                        <BuiltMeshes built={f.built} night={sky.night} />
                    </group>
                ))}
            </group>
            {arrange?.selected
                ? furniBuilt
                      .filter((f) => f.id === arrange.selected)
                      .map((f) => (
                          <mesh
                              key="picked"
                              position={[
                                  f.prop.at[0] + f.prop.size[0] / 2,
                                  standHeight(scene, {
                                      x: f.prop.at[0],
                                      y: f.prop.at[1],
                                  }) + 0.02,
                                  f.prop.at[1] + f.prop.size[1] / 2,
                              ]}
                              rotation={[-Math.PI / 2, 0, 0]}
                          >
                              <ringGeometry
                                  args={[
                                      0.55 * Math.max(...f.prop.size),
                                      0.65 * Math.max(...f.prop.size),
                                      4,
                                      1,
                                      Math.PI / 4,
                                  ]}
                              />
                              <meshBasicMaterial color="#3b82f6" />
                          </mesh>
                      ))
                : null}
            {scene.def.outdoor ? <Water scene={scene} sky={sky} /> : null}
            <PropLights
                scene={scene}
                props={[
                    ...scene.props.filter((p) => !p.furniId),
                    ...furniProps.map((f) => f.prop),
                ]}
                sky={sky}
            />
            {furniProps.map(({ prop, view }) =>
                view?.notable ? (
                    <Html
                        key={prop.furniId}
                        position={[
                            prop.at[0] + prop.size[0] / 2,
                            1.5 + (prop.y ?? 0),
                            prop.at[1] + prop.size[1] / 2,
                        ]}
                        center
                        zIndexRange={[20, 0]}
                    >
                        <span
                            title={view.notable ?? ''}
                            style={{
                                fontSize: 14,
                                pointerEvents: 'auto',
                                cursor: 'help',
                            }}
                        >
                            ❗
                        </span>
                    </Html>
                ) : null,
            )}
            {hover && (onSpotClick || arrange) ? (
                <Hover scene={scene} tile={hover} />
            ) : null}
            <Bodies
                scene={walkable}
                place={place}
                places={places}
                bodies={bodies}
                playerId={playerId}
                allEars={props.allEars}
                onBodyClick={onBodyClick}
            />
        </>
    );
}

/** A prop's prefab, variant and world position. */
function placement(
    scene: Scene,
    p: PlacedProp,
    state?: { condition: number; on: boolean; notable: boolean },
): Placement {
    const x = p.at[0] + p.size[0] / 2;
    const z = p.at[1] + p.size[1] / 2;
    const ground = Math.max(0, standHeight(scene, { x: p.at[0], y: p.at[1] }));
    const onWater = scene.cells[p.at[1]]?.[p.at[0]]?.ground === 'water';
    return {
        prefab: p.prefab,
        variant: { seed: p.seed, tint: p.tint ?? {}, text: p.text, state },
        x,
        z,
        y: (onWater ? 0 : ground) + (p.y ?? 0),
        rot: p.rot,
    };
}

function BuiltMeshes({ built, night }: { built: Built; night: boolean }) {
    return (
        <>
            {built.plain ? (
                <mesh
                    geometry={built.plain}
                    material={plainMat}
                    castShadow
                    receiveShadow
                />
            ) : null}
            {built.glow ? (
                <mesh geometry={built.glow} material={glowMat} />
            ) : null}
            {built.night ? (
                <mesh
                    geometry={built.night}
                    material={night ? glowMat : plainMat}
                    castShadow={!night}
                />
            ) : null}
            {built.clear.map((c) => (
                <mesh key={c.geometry.uuid} geometry={c.geometry}>
                    <meshStandardMaterial
                        vertexColors
                        transparent
                        opacity={c.opacity}
                    />
                </mesh>
            ))}
            {built.signs.map((s) => (
                <Sign key={`${s.text}:${s.matrix.elements.join(',')}`} {...s} />
            ))}
            {built.tagged.map((t, i) => (
                <Tagged key={`${t.tag}:${i}:${t.geometry.uuid}`} {...t} />
            ))}
        </>
    );
}

function Sign({ w, h, text, bg, fg, matrix }: Built['signs'][number]) {
    const texture = useMemo(() => {
        const c = document.createElement('canvas');
        c.width = 256;
        c.height = Math.max(32, Math.round((256 * h) / w));
        const ctx = c.getContext('2d');
        if (ctx) {
            ctx.fillStyle = bg;
            ctx.fillRect(0, 0, c.width, c.height);
            ctx.fillStyle = fg;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            let size = c.height * 0.62;
            ctx.font = `bold ${size}px Georgia, serif`;
            while (ctx.measureText(text).width > c.width * 0.9 && size > 8) {
                size -= 2;
                ctx.font = `bold ${size}px Georgia, serif`;
            }
            ctx.fillText(text, c.width / 2, c.height / 2 + 2);
        }
        const t = new THREE.CanvasTexture(c);
        t.colorSpace = THREE.SRGBColorSpace;
        return t;
    }, [w, h, text, bg, fg]);
    const m = useMemo(() => {
        const pos = new THREE.Vector3();
        const q = new THREE.Quaternion();
        const s = new THREE.Vector3();
        matrix.decompose(pos, q, s);
        return { pos, q };
    }, [matrix]);
    return (
        <mesh position={m.pos} quaternion={m.q}>
            <planeGeometry args={[w, h]} />
            <meshBasicMaterial map={texture} />
        </mesh>
    );
}

/** Parts that move: flickering lamps, smoke from a broken machine, a jukebox's lights and notes, ducks. */
function Tagged({ tag, geometry, colour, at }: Built['tagged'][number]) {
    const ref = useRef<THREE.Mesh>(null);
    const mat = useRef<THREE.MeshBasicMaterial>(null);
    const seed = at.x * 3.1 + at.z * 1.7;
    useFrame(({ clock }) => {
        const t = clock.elapsedTime;
        const m = ref.current;
        if (!m) return;
        if (tag === 'flicker')
            m.visible = Math.sin(t * 23 + seed) + Math.sin(t * 7.3) > -0.3;
        if (tag === 'disco' && mat.current)
            mat.current.color.setHSL((t * 0.25) % 1, 0.8, 0.6);
        if (tag === 'smoke') {
            const k = (t * 0.5 + seed) % 1;
            m.position.set(0, k * 0.8, 0);
            m.scale.setScalar(0.6 + k);
            if (mat.current) mat.current.opacity = 0.7 * (1 - k);
        }
        if (tag === 'paddle')
            m.position.set(
                Math.sin(t * 0.21 + seed) * 0.7,
                Math.sin(t * 2 + seed) * 0.015,
                Math.cos(t * 0.17 + seed) * 0.35,
            );
    });
    if (tag === 'notes')
        return (
            <Html position={at} center zIndexRange={[15, 0]}>
                <span
                    className="places-notes"
                    style={{
                        fontSize: 14,
                        color: '#2d2d6b',
                        animation: 'places-notes 2.4s linear infinite',
                    }}
                >
                    ♪ ♫
                </span>
            </Html>
        );
    const glows = tag === 'flicker' || tag === 'light' || tag === 'disco';
    return (
        <mesh ref={ref} geometry={geometry} castShadow={tag === 'paddle'}>
            {glows || tag === 'smoke' ? (
                <meshBasicMaterial
                    ref={mat}
                    color={colour}
                    transparent={tag === 'smoke'}
                />
            ) : (
                <meshStandardMaterial color={colour} flatShading />
            )}
        </mesh>
    );
}

/** Lamps light the night (and rooms always), a handful at a time. */
function PropLights({
    scene,
    props,
    sky,
}: {
    scene: Scene;
    props: PlacedProp[];
    sky: Sky;
}) {
    const indoor = !scene.def.outdoor;
    if (!indoor && !sky.night) return null;
    const lit = props.filter((p) => p.prefab.light).slice(0, 8);
    return (
        <>
            {lit.map((p) => {
                const pl = placement(scene, p);
                const l = p.prefab.light;
                if (!l) return null;
                return (
                    <pointLight
                        key={`${p.p}:${p.at.join(',')}`}
                        position={[
                            pl.x + l.at[0],
                            pl.y + l.at[1],
                            pl.z + l.at[2],
                        ]}
                        color={l.colour}
                        intensity={l.power * (sky.night ? 1.6 : 0.5)}
                        distance={7}
                        decay={1.6}
                    />
                );
            })}
        </>
    );
}

const waterVert = `
varying vec3 vPos;
void main() {
    vec4 w = modelMatrix * vec4(position, 1.0);
    vPos = w.xyz;
    gl_Position = projectionMatrix * viewMatrix * w;
}`;

const waterFrag = `
uniform float uTime;
uniform vec3 uDeep;
uniform vec3 uLight;
varying vec3 vPos;
void main() {
    float a = sin(vPos.x * 2.7 + uTime * 0.9) * sin(vPos.z * 2.1 - uTime * 0.7);
    float b = sin((vPos.x - vPos.z) * 6.0 - uTime * 1.8);
    float t = clamp(0.45 + 0.3 * a + 0.12 * b, 0.0, 1.0);
    vec3 col = mix(uDeep, uLight, t);
    float glint = smoothstep(0.93, 1.0, sin(vPos.x * 9.0 + uTime * 1.3) * sin(vPos.z * 7.0 - uTime));
    col += vec3(0.35) * glint;
    gl_FragColor = vec4(col, 1.0);
    #include <colorspace_fragment>
}`;

function Water({ scene, sky }: { scene: Scene; sky: Sky }) {
    const mat = useRef<THREE.ShaderMaterial>(null);
    const uniforms = useMemo(
        () => ({
            uTime: { value: 0 },
            uDeep: { value: new THREE.Color() },
            uLight: { value: new THREE.Color() },
        }),
        [],
    );
    uniforms.uDeep.value.set(sky.water[0]);
    uniforms.uLight.value.set(sky.water[1]);
    useFrame(({ clock }) => {
        uniforms.uTime.value = clock.elapsedTime;
    });
    return (
        <mesh
            position={[scene.w / 2, -0.42, scene.h / 2]}
            rotation={[-Math.PI / 2, 0, 0]}
            receiveShadow
        >
            <planeGeometry args={[scene.w, scene.h]} />
            <shaderMaterial
                ref={mat}
                vertexShader={waterVert}
                fragmentShader={waterFrag}
                uniforms={uniforms}
            />
        </mesh>
    );
}

function Hover({ scene, tile }: { scene: Scene; tile: Pt }) {
    if (
        tile.x < 0 ||
        tile.y < 0 ||
        tile.x >= scene.w ||
        tile.y >= scene.h ||
        scene.blocked.has(`${tile.x},${tile.y}`)
    )
        return null;
    return (
        <mesh
            position={[
                tile.x + 0.5,
                standHeight(scene, tile) + 0.015,
                tile.y + 0.5,
            ]}
            rotation={[-Math.PI / 2, 0, 0]}
        >
            <ringGeometry args={[0.6, 0.68, 4, 1, Math.PI / 4]} />
            <meshBasicMaterial color="#ffffff" transparent opacity={0.85} />
        </mesh>
    );
}

interface BodiesProps {
    scene: Scene;
    place: Place;
    places: Place[];
    bodies: Body[];
    playerId: string | null;
    allEars?: boolean;
    onBodyClick?: (participantId: string) => void;
}

/** Everyone here or passing through the door, with their talk overhead. */
function Bodies({
    scene,
    place,
    places,
    bodies,
    playerId,
    allEars,
    onBodyClick,
}: BodiesProps) {
    const messages = useChatGroupMessages(place.talkRoomId ?? '');
    const now = useNow(1000);
    const walks = useRef(new Map<string, Walk>());
    const shown = useRef(new Map<string, Pt>());
    const slots = useMemo(
        () => targets(scene, place.id, bodies),
        [scene, place.id, bodies],
    );
    const sceneOf = useMemo(() => {
        const byId = new Map(places.map((p) => [p.id, p.scene ?? undefined]));
        return (id: string | null | undefined) =>
            id ? byId.get(id) : undefined;
    }, [places]);

    // A Body whose slot moved walks there from where it's drawn now.
    useEffect(() => {
        for (const [id, slot] of slots) {
            const from = shown.current.get(id);
            if (!from) continue;
            const last = walks.current.get(id)?.path.at(-1);
            if (last && last.x === slot.tile.x && last.y === slot.tile.y)
                continue;
            const start = round(from);
            if (start.x === slot.tile.x && start.y === slot.tile.y) continue;
            walks.current.set(id, {
                path: pathIn(scene, start, slot.tile),
                startedAt: Date.now(),
            });
        }
    }, [slots, scene]);

    const recent = messages
        .filter(
            (m) => m.content?.trim() && m.sendAt && now - m.sendAt < BUBBLE_MS,
        )
        .slice(-8);

    return (
        <>
            {bodies.map((b) => {
                const id = b.participantId ?? '';
                const look = lookOf(
                    b.appearance,
                    avatarColour(b.participantId),
                );
                const mine = recent.filter((m) => m.senderId === id).slice(-2);
                const busy = b.activity ?? '';
                const first = (b.name ?? '').split(' ')[0];
                const isMe = id === playerId;
                const state = (): AvatarState | null => {
                    const pose = poseOf(
                        b,
                        scene,
                        place.id,
                        sceneOf,
                        walks.current,
                        shown.current,
                        slots,
                        Date.now(),
                    );
                    if (!pose) return null;
                    shown.current.set(id, pose.tile);
                    if (!pose.moving && walks.current.has(id))
                        walks.current.delete(id);
                    return {
                        pose,
                        ground: standHeight(scene, round(pose.tile)),
                        asleep: !!b.asleep,
                        lying: !!b.asleep || b.posture === 'Lying',
                        phone: busy.includes('phone'),
                        hangout: b.pose ?? null,
                        partner: b.poseWith
                            ? (shown.current.get(b.poseWith) ?? null)
                            : null,
                        talking: mine.some(
                            (m) => Date.now() - (m.sendAt ?? 0) < 3000,
                        ),
                    };
                };
                const here =
                    (b.destPlaceId ?? b.placeId) === place.id ||
                    b.placeId === place.id;
                if (!here) return null;
                return (
                    <Avatar
                        key={id}
                        look={look}
                        isMe={isMe}
                        state={state}
                        onClick={
                            onBodyClick ? () => onBodyClick(id) : undefined
                        }
                    >
                        <Html
                            position={[
                                0,
                                b.asleep || b.posture === 'Lying' ? 0.9 : 1.62,
                                0,
                            ]}
                            center
                            zIndexRange={[30, 0]}
                            style={{ pointerEvents: 'none' }}
                        >
                            <Overhead
                                name={isMe ? `${first} (you)` : first}
                                asleep={!!b.asleep}
                                icon={
                                    busy.includes('laptop')
                                        ? '💻'
                                        : busy.includes('diary')
                                          ? '📓'
                                          : busy && !busy.includes('phone')
                                            ? '✋'
                                            : ''
                                }
                                bubbles={mine.map((m) => {
                                    const heard = canHear(m, playerId, allEars);
                                    return {
                                        id: String(m.messageId ?? m.sendAt),
                                        text: heard
                                            ? (m.content ?? '')
                                            : scribble(m.content),
                                        heard,
                                        volume: volumeOf(m),
                                        age:
                                            (now - (m.sendAt ?? now)) /
                                            BUBBLE_MS,
                                    };
                                })}
                            />
                        </Html>
                    </Avatar>
                );
            })}
        </>
    );
}

interface Bubble {
    id: string;
    text: string;
    heard: boolean;
    volume: string;
    age: number;
}

function Overhead({
    name,
    bubbles,
    asleep,
    icon,
}: {
    name: string;
    bubbles: Bubble[];
    asleep: boolean;
    icon: string;
}) {
    return (
        <div
            style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: 3,
                transform: 'translateY(-50%)',
                whiteSpace: 'nowrap',
            }}
        >
            {bubbles.map((b) => (
                <div
                    key={b.id}
                    style={{
                        width: 'max-content',
                        maxWidth: 220,
                        whiteSpace: 'normal',
                        background: b.heard ? '#fff' : '#eee',
                        color: b.heard ? '#111' : '#777',
                        border: `1px ${b.volume === 'whisper' ? 'dashed' : 'solid'} #444`,
                        borderRadius: 7,
                        padding: '2px 7px',
                        font: `${b.volume === 'whisper' ? 'italic ' : ''}${b.volume === 'shout' ? 'bold 13px' : '11px'} Tahoma, sans-serif`,
                        opacity:
                            Math.max(0.25, 1 - b.age * 0.8) *
                            (b.heard ? 1 : 0.7),
                        transform: `translateY(${-b.age * 18}px)`,
                        boxShadow: '0 1px 0 rgba(0,0,0,.25)',
                        textAlign: 'left',
                    }}
                >
                    {b.text.length > 120 ? `${b.text.slice(0, 119)}…` : b.text}
                </div>
            ))}
            <div
                style={{
                    font: 'bold 10px Tahoma, sans-serif',
                    color: '#fff',
                    background: 'rgba(0,0,0,.45)',
                    borderRadius: 3,
                    padding: '0 4px',
                }}
            >
                {asleep ? '💤 ' : ''}
                {name}
                {icon ? ` ${icon}` : ''}
            </div>
        </div>
    );
}
