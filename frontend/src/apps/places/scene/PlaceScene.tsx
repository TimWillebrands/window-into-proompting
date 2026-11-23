import { HudPanel, Icon, Toolbar, ToolbarButton } from '@partytown/ui';
import { MapControls, OrthographicCamera } from '@react-three/drei';
import {
    Canvas,
    type ThreeEvent,
    useFrame,
    useThree,
} from '@react-three/fiber';
import {
    type CSSProperties,
    createContext,
    memo,
    type ReactNode,
    type RefObject,
    useCallback,
    useContext,
    useEffect,
    useMemo,
    useRef,
    useState,
} from 'react';
import * as THREE from 'three';
import {
    instancedBufferAttribute,
    mix,
    positionWorld,
    sin,
    smoothstep,
    uniform,
} from 'three/tsl';
import { MeshBasicNodeMaterial, PointsNodeMaterial } from 'three/webgpu';
import { useStoreWithEqualityFn } from 'zustand/traditional';
import { useRealtimeStore } from '#lib/realtime-store';
import type { Body } from '../../../api/model/body';
import type { FurniView } from '../../../api/model/furniView';
import type { Place } from '../../../api/model/place';
import type { RealtimeChatMessage } from '../../../lib/room-state';
import { type Look, lookOf } from '../iso/appearance';
import type { Pt } from '../iso/iso';
import {
    avatarColour,
    canHear,
    scribble,
    serverNow,
    toMs,
    volumeOf,
} from '../places-lib';
import Avatar, {
    type AvatarState,
    AvatarWarmup,
    avatarMaterials,
} from './Avatar';
import { type Built, build, type Placement } from './build';
import { canvasRenderer } from './gpu';
import { buildGround } from './ground';
import { useMeshVersion } from './meshes';
import { poseOf, round, targets, type Walk } from './motion';
import { PIXEL_DPR, PIXEL_STYLE, Pixelate, usePixelDpr } from './pixel';
import './scene.css';
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
    hour?: number;
    hud?: ReactNode;
    arrange?: {
        selected: string | null;
        onPick: (furniId: string) => void;
        onTile: (tile: Pt) => void;
    };
}

const MIN_HEIGHT = 240;

export default function PlaceScene(props: Props) {
    const { place, furni } = props;
    const scene = useMemo(() => sceneFor(place), [place]);
    const wrap = useRef<HTMLDivElement>(null);
    const [width, setWidth] = useState(640);
    const [height, setHeight] = useState(MIN_HEIGHT);
    const [zoomBy, setZoomBy] = useState(1);
    const [recentre, setRecentre] = useState(0);
    const hour = useHour(props.hour);
    const dpr = usePixelDpr();
    const sky = skyAt(hour, !!scene.def.outdoor, scene.def.light);
    useEffect(() => {
        const el = wrap.current;
        if (!el) return;
        const measure = () => {
            setWidth(el.clientWidth);
            setHeight(Math.max(MIN_HEIGHT, el.clientHeight));
        };
        const ro = new ResizeObserver(measure);
        ro.observe(el);
        measure();
        return () => ro.disconnect();
    }, []);
    const here = furni.filter((f) => f.placeId === place.id);
    const overlay = useMemo<Overlay>(
        () => ({ anchors: new Map(), labels: new Map() }),
        [],
    );

    return (
        <OverlayContext.Provider value={overlay}>
            <div
                ref={wrap}
                className="pl-view"
                style={
                    {
                        width: '100%',
                        flex: `1 0 ${MIN_HEIGHT}px`,
                        minHeight: MIN_HEIGHT,
                        '--pl-sky': sky.backdrop,
                    } as CSSProperties
                }
            >
                <Canvas
                    shadows="basic"
                    flat
                    dpr={dpr}
                    gl={PLACE_GL}
                    style={{
                        ...PIXEL_STYLE,
                        cursor:
                            props.onSpotClick || props.arrange
                                ? 'pointer'
                                : 'default',
                    }}
                >
                    <Rig
                        scene={scene}
                        width={width}
                        height={height}
                        zoomBy={zoomBy}
                        recentre={recentre}
                        focus={focusOf(scene, props)}
                        mine={isMine(props)}
                    />
                    <Lights scene={scene} sky={sky} />
                    <Warmup />
                    <World {...props} scene={scene} sky={sky} furni={here} />
                    <Projector />
                    <Pixelate />
                </Canvas>
                <Labels {...props} overlay={overlay} />
                <div className="pointer-events-none absolute inset-0">
                    {props.hud}
                    <HudPanel anchor="top-end">
                        <Toolbar orientation="vertical" aria-label="Zoom">
                            <ToolbarButton
                                title="Zoom in"
                                aria-label="Zoom in"
                                icon={<Icon name="zoom-in" />}
                                onClick={() =>
                                    setZoomBy((z) => Math.min(2.5, z * 1.25))
                                }
                            />
                            <ToolbarButton
                                title="Zoom out"
                                aria-label="Zoom out"
                                icon={<Icon name="zoom-out" />}
                                onClick={() =>
                                    setZoomBy((z) => Math.max(0.4, z / 1.25))
                                }
                            />
                            <ToolbarButton
                                title="Back to you"
                                aria-label="Back to you"
                                icon={<Icon name="locate" />}
                                onClick={() => setRecentre((n) => n + 1)}
                            />
                        </Toolbar>
                    </HudPanel>
                </div>
            </div>
        </OverlayContext.Provider>
    );
}

const isDoable = (f: FurniView) =>
    !!f.useLabel && (f as { gives?: string | null }).gives != null;

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

const isMine = ({ bodies, playerId, place }: Props) =>
    bodies.some(
        (b) =>
            b.participantId === playerId &&
            (b.destPlaceId ?? b.placeId) === place.id,
    );

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

function Rig({
    scene,
    width,
    height,
    zoomBy,
    recentre,
    focus,
    mine,
}: {
    scene: Scene;
    width: number;
    height: number;
    zoomBy: number;
    recentre: number;
    focus: Pt;
    mine: boolean;
}) {
    const cam = useRef<THREE.OrthographicCamera>(null);
    const [camera, setCamera] = useState<THREE.OrthographicCamera | null>(null);
    useEffect(() => setCamera(cam.current), []);
    const controls = useRef<React.ComponentRef<typeof MapControls>>(null);
    const at = useRef(focus);
    at.current = focus;
    // biome-ignore lint/correctness/useExhaustiveDependencies: recentre and mine are triggers
    useEffect(() => {
        const fx = at.current.x;
        const fy = at.current.y;
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
            ctl.minZoom = zoom * 0.4;
            ctl.maxZoom = zoom * 2.5;
            ctl.target.copy(target);
            ctl.update();
        }
    }, [scene, width, height, zoomBy, recentre, mine, camera]);
    return (
        <>
            <OrthographicCamera ref={cam} makeDefault near={-100} far={300} />
            {camera ? (
                <MapControls
                    ref={controls}
                    camera={camera}
                    enableRotate={false}
                    enableZoom
                    zoomToCursor
                    zoomSpeed={0.8}
                    screenSpacePanning
                    enablePan
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
            <ambientLight intensity={sky.ambient} color={sky.ambientColour} />
            <hemisphereLight
                args={['#cfe3ff', '#7a6a55', sky.night ? 0.18 : 0.3]}
            />
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
const sunlitMat = new THREE.MeshBasicMaterial({
    color: '#fff3d6',
    transparent: true,
    opacity: 0.16,
    depthWrite: false,
    blending: THREE.AdditiveBlending,
});
const clearMats = new Map<number, THREE.Material>();
function clearMat(opacity: number): THREE.Material {
    let m = clearMats.get(opacity);
    if (!m) {
        m = new THREE.MeshStandardMaterial({
            vertexColors: true,
            transparent: true,
            opacity,
        });
        clearMats.set(opacity, m);
    }
    return m;
}

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
    const meshes = useMeshVersion();
    // biome-ignore lint/correctness/useExhaustiveDependencies: meshes bumps when a model lands
    const statics = useMemo(
        () =>
            build([
                ...ground.houses,
                ...scene.props
                    .filter((p) => !p.furniId)
                    .map((p) => placement(scene, p)),
            ]),
        [scene, ground, meshes],
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
    // biome-ignore lint/correctness/useExhaustiveDependencies: meshes bumps when a model lands
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
        [furniProps, scene, meshes],
    );
    const { arrange } = props;
    const placed = useMemo(() => furniProps.map((f) => f.prop), [furniProps]);
    const lamps = useMemo(
        () => [...scene.props.filter((p) => !p.furniId), ...placed],
        [scene, placed],
    );
    const walkable = useMemo(() => withFurni(scene, placed), [scene, placed]);
    const hoverAt = useRef<(tile: Pt | null) => void>(() => {});
    const tileOf = (e: ThreeEvent<PointerEvent | MouseEvent>): Pt => ({
        x: Math.floor(e.point.x),
        y: Math.floor(e.point.z),
    });

    return (
        <>
            {/* biome-ignore lint/a11y/noStaticElementInteractions: a three.js mesh, not DOM */}
            <mesh
                position={[scene.w / 2, 0, scene.h / 2]}
                rotation={[-Math.PI / 2, 0, 0]}
                onPointerMove={(e) => {
                    e.stopPropagation();
                    hoverAt.current(tileOf(e));
                }}
                onPointerOut={() => hoverAt.current(null)}
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
                <planeGeometry args={[scene.w, scene.h]} />
                <meshBasicMaterial visible={false} />
            </mesh>
            <group>
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
                {ground.sunlit && !sky.night ? (
                    <mesh geometry={ground.sunlit} material={sunlitMat} />
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
            {scene.def.ambience ? (
                <Ambience
                    kind={scene.def.ambience}
                    scene={scene}
                    night={sky.night}
                />
            ) : null}
            <PropLights scene={scene} props={lamps} sky={sky} />
            <FurniAnchors props={placed} />
            {onSpotClick || arrange ? (
                <Hover scene={scene} at={hoverAt} />
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
                <mesh
                    key={c.geometry.uuid}
                    geometry={c.geometry}
                    material={clearMat(c.opacity)}
                />
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

function Tagged({
    tag,
    geometry,
    colour,
    at,
    glow,
    painted,
}: Built['tagged'][number]) {
    const ref = useRef<THREE.Mesh>(null);
    const mat = useRef<THREE.MeshBasicMaterial>(null);
    const seed = at.x * 3.1 + at.z * 1.7;
    const base = useMemo(() => new THREE.Color(colour), [colour]);
    useFrame(({ clock }) => {
        const t = clock.elapsedTime;
        const m = ref.current;
        if (!m) return;
        if (tag === 'flicker')
            m.visible = Math.sin(t * 23 + seed) + Math.sin(t * 7.3) > -0.3;
        if (tag === 'disco' && mat.current)
            mat.current.color.setHSL((t * 0.25 + seed * 0.13) % 1, 0.8, 0.6);
        if (tag === 'smoke') {
            const k = (t * 0.5 + seed) % 1;
            m.position.set(at.x, at.y + k * 0.8, at.z);
            m.scale.setScalar(0.6 + k);
            if (mat.current) mat.current.opacity = 0.7 * (1 - k);
        }
        if (tag === 'paddle')
            m.position.set(
                at.x + Math.sin(t * 0.21 + seed) * 0.7,
                at.y + Math.sin(t * 2 + seed) * 0.015,
                at.z + Math.cos(t * 0.17 + seed) * 0.35,
            );
        if (tag === 'spin') m.rotation.y = t * 1.2 + seed;
        if (tag === 'bob')
            m.position.y = at.y + Math.sin(t * 1.6 + seed) * 0.04;
        if (tag === 'sway') {
            m.rotation.z = Math.sin(t * 0.9 + seed) * 0.06;
            m.rotation.x = Math.sin(t * 0.7 + seed * 2) * 0.04;
        }
        if (tag === 'flame') {
            const k = Math.sin(t * 11 + seed) * 0.5 + Math.sin(t * 17.3 + seed);
            m.scale.set(1 - k * 0.06, 1 + k * 0.12, 1 - k * 0.06);
        }
        if (tag === 'pulse' && mat.current)
            mat.current.color
                .copy(base)
                .multiplyScalar(0.65 + 0.35 * Math.sin(t * 2 + seed));
        if (tag === 'twinkle' && mat.current)
            mat.current.color
                .copy(base)
                .multiplyScalar(
                    Math.sin(t * 3.1 + seed * 5) * Math.sin(t * 1.3 + seed) >
                        0.2
                        ? 1
                        : 0.35,
                );
    });
    if (tag === 'notes') return null;
    const glows =
        glow ||
        tag === 'flicker' ||
        tag === 'light' ||
        tag === 'disco' ||
        tag === 'flame' ||
        tag === 'pulse' ||
        tag === 'twinkle';
    return (
        <mesh
            ref={ref}
            geometry={geometry}
            position={at}
            castShadow={!glows && tag !== 'smoke'}
        >
            {glows || tag === 'smoke' ? (
                <meshBasicMaterial
                    ref={mat}
                    color={colour}
                    vertexColors={!!painted}
                    transparent={tag === 'smoke'}
                />
            ) : (
                <meshStandardMaterial
                    color={colour}
                    vertexColors={!!painted}
                    flatShading
                />
            )}
        </mesh>
    );
}

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
    const lit = props.filter((p) => p.prefab.light).slice(0, 12);
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

function waterMaterial() {
    const time = uniform(0);
    const deep = uniform(new THREE.Color());
    const light = uniform(new THREE.Color());
    const p = positionWorld;
    const a = sin(p.x.mul(2.7).add(time.mul(0.9))).mul(
        sin(p.z.mul(2.1).sub(time.mul(0.7))),
    );
    const b = sin(p.x.sub(p.z).mul(6).sub(time.mul(1.8)));
    const t = a.mul(0.3).add(b.mul(0.12)).add(0.45).clamp(0, 1);
    const glint = smoothstep(
        0.93,
        1,
        sin(p.x.mul(9).add(time.mul(1.3))).mul(sin(p.z.mul(7).sub(time))),
    );
    const material = new MeshBasicNodeMaterial();
    material.colorNode = mix(deep, light, t).add(glint.mul(0.35));
    return { material, time, deep, light };
}

function Water({ scene, sky }: { scene: Scene; sky: Sky }) {
    const water = useMemo(waterMaterial, []);
    useEffect(() => () => water.material.dispose(), [water]);
    water.deep.value.set(sky.water[0]);
    water.light.value.set(sky.water[1]);
    useFrame(({ clock }) => {
        water.time.value = clock.elapsedTime;
    });
    return (
        <mesh
            position={[scene.w / 2, -0.42, scene.h / 2]}
            rotation={[-Math.PI / 2, 0, 0]}
            receiveShadow
            material={water.material}
        >
            <planeGeometry args={[scene.w, scene.h]} />
        </mesh>
    );
}

const PLACE_GL = canvasRenderer({ antialias: false });

const AIR: Record<
    string,
    {
        n: number;
        size: number;
        colours: string[];
        fall: number;
        drift: number;
        top: number;
        glow?: boolean;
    }
> = {
    dust: {
        n: 0.35,
        size: 2,
        colours: ['#fff6dc'],
        fall: -0.03,
        drift: 0.12,
        top: 2.2,
    },
    fireflies: {
        n: 0.25,
        size: 3,
        colours: ['#e9ff70', '#b8ff5c'],
        fall: 0,
        drift: 0.35,
        top: 1.6,
        glow: true,
    },
    snow: {
        n: 1.2,
        size: 3,
        colours: ['#ffffff'],
        fall: -0.55,
        drift: 0.2,
        top: 5,
    },
    rain: { n: 1.6, size: 2, colours: ['#9fc4e8'], fall: -5, drift: 0, top: 5 },
    leaves: {
        n: 0.3,
        size: 4,
        colours: ['#d9822b', '#b5562f', '#e0b23c'],
        fall: -0.35,
        drift: 0.5,
        top: 4,
    },
    petals: {
        n: 0.35,
        size: 3,
        colours: ['#ffc2d9', '#ff9ec4', '#ffffff'],
        fall: -0.25,
        drift: 0.45,
        top: 4,
    },
    embers: {
        n: 0.3,
        size: 2,
        colours: ['#ff9a3c', '#ffd27a'],
        fall: 0.4,
        drift: 0.2,
        top: 2.6,
        glow: true,
    },
    bubbles: {
        n: 0.4,
        size: 3,
        colours: ['#d6f1ff'],
        fall: 0.3,
        drift: 0.15,
        top: 2.8,
    },
    confetti: {
        n: 0.9,
        size: 3,
        colours: ['#ff5d8f', '#ffd23f', '#3bceac', '#6a8dff', '#ffffff'],
        fall: -0.7,
        drift: 0.4,
        top: 4,
    },
};

function Ambience({
    kind,
    scene,
    night,
}: {
    kind: string;
    scene: Scene;
    night: boolean;
}) {
    const spec = AIR[kind];
    const dpr = useThree((s) => s.viewport.dpr);
    const made = useMemo(() => {
        if (!spec) return null;
        const n = Math.min(400, Math.ceil(scene.w * scene.h * spec.n));
        const pos = new Float32Array(n * 3);
        const col = new Float32Array(n * 3);
        const seeds = new Float32Array(n);
        const c = new THREE.Color();
        for (let i = 0; i < n; i++) {
            pos.set(
                [
                    Math.random() * scene.w,
                    Math.random() * spec.top,
                    Math.random() * scene.h,
                ],
                i * 3,
            );
            c.set(spec.colours[i % spec.colours.length]);
            col.set([c.r, c.g, c.b], i * 3);
            seeds[i] = Math.random() * 100;
        }
        const at = new THREE.InstancedBufferAttribute(pos, 3);
        at.setUsage(THREE.DynamicDrawUsage);
        const material = new PointsNodeMaterial({
            sizeAttenuation: false,
            transparent: true,
            depthWrite: false,
        });
        material.positionNode = instancedBufferAttribute(at);
        material.colorNode = instancedBufferAttribute(
            new THREE.InstancedBufferAttribute(col, 3),
        );
        const sprite = new THREE.Sprite(material);
        sprite.count = n;
        sprite.frustumCulled = false;
        sprite.raycast = () => {};
        return { at, material, sprite, seeds, n };
    }, [spec, scene.w, scene.h]);
    useEffect(() => () => made?.material.dispose(), [made]);
    if (made && spec) {
        made.material.size = (spec.size * PIXEL_DPR) / dpr;
        made.material.opacity = spec.glow ? (night ? 1 : 0.5) : 0.85;
    }
    useFrame(({ clock }, dt) => {
        if (!made || !spec) return;
        const t = clock.elapsedTime;
        const a = made.at;
        const p = a.array as Float32Array;
        const step = Math.min(dt, 0.1);
        for (let i = 0; i < made.n; i++) {
            const s = made.seeds[i];
            p[i * 3] += Math.sin(t * 0.6 + s) * spec.drift * step;
            p[i * 3 + 2] += Math.cos(t * 0.5 + s * 1.3) * spec.drift * step;
            p[i * 3 + 1] +=
                spec.fall * step +
                (spec.fall === 0 ? Math.sin(t + s) * 0.1 * step : 0);
            if (p[i * 3 + 1] < 0.05) p[i * 3 + 1] = spec.top;
            if (p[i * 3 + 1] > spec.top) p[i * 3 + 1] = 0.05;
        }
        a.needsUpdate = true;
    });
    if (!made || !spec) return null;
    return <primitive object={made.sprite} />;
}

function Hover({
    scene,
    at,
}: {
    scene: Scene;
    at: RefObject<(tile: Pt | null) => void>;
}) {
    const [tile, setTile] = useState<Pt | null>(null);
    useEffect(() => {
        at.current = (t) =>
            setTile((p) => (p && t && p.x === t.x && p.y === t.y ? p : t));
        return () => {
            at.current = () => {};
        };
    }, [at]);
    const shown =
        !!tile &&
        tile.x >= 0 &&
        tile.y >= 0 &&
        tile.x < scene.w &&
        tile.y < scene.h &&
        !scene.blocked.has(`${tile.x},${tile.y}`);
    return (
        <mesh
            visible={shown}
            geometry={hoverRing}
            material={hoverMat}
            position={
                tile
                    ? [
                          tile.x + 0.5,
                          standHeight(scene, tile) + 0.015,
                          tile.y + 0.5,
                      ]
                    : [0, -10, 0]
            }
            rotation={[-Math.PI / 2, 0, 0]}
        />
    );
}

function Warmup() {
    const materials = useMemo(
        () => [...avatarMaterials(), hoverMat, beaconMat, basicMat],
        [],
    );
    return (
        <group scale={0.0001}>
            {materials.map((m) => (
                <mesh
                    key={m.uuid}
                    geometry={warmBox}
                    material={m}
                    frustumCulled={false}
                />
            ))}
            <AvatarWarmup />
        </group>
    );
}

const warmBox = new THREE.BoxGeometry(1, 1, 1);
const beaconRing = new THREE.RingGeometry(0.3, 0.48, 32);
const beaconMat = new THREE.MeshBasicMaterial({
    color: '#5ec8ff',
    transparent: true,
    depthWrite: false,
});
const basicMat = new THREE.MeshBasicMaterial({ color: '#ffffff' });

const hoverRing = new THREE.RingGeometry(0.6, 0.68, 4, 1, Math.PI / 4);
const hoverMat = new THREE.MeshBasicMaterial({
    color: '#ffffff',
    transparent: true,
    opacity: 0.85,
});

interface BodiesProps {
    scene: Scene;
    place: Place;
    places: Place[];
    bodies: Body[];
    playerId: string | null;
    allEars?: boolean;
    onBodyClick?: (participantId: string) => void;
}

function Bodies({
    scene,
    place,
    places,
    bodies,
    playerId,
    onBodyClick,
}: BodiesProps) {
    const spoken = useSpoken(place.talkRoomId ?? '');
    const walks = useRef(new Map<string, Walk>());
    const shown = useRef(new Map<string, Pt>());
    const states = useRef(new Map<string, () => AvatarState | null>());
    const click = useRef(onBodyClick);
    click.current = onBodyClick;
    const slots = useMemo(
        () => targets(scene, place.id, bodies),
        [scene, place.id, bodies],
    );
    const sceneOf = useMemo(() => {
        const byId = new Map(places.map((p) => [p.id, p.scene ?? undefined]));
        return (id: string | null | undefined) =>
            id ? byId.get(id) : undefined;
    }, [places]);

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
                startedAt: serverNow(),
            });
        }
    }, [slots, scene]);

    const me = bodies.find((b) => b.participantId === playerId);
    const coming =
        me && me.destPlaceId === place.id && me.placeId !== place.id
            ? slots.get(playerId ?? '')
            : undefined;

    return (
        <>
            {coming ? (
                <Beacon
                    tile={coming.tile}
                    ground={standHeight(scene, coming.tile)}
                />
            ) : null}
            {bodies.map((b) => {
                const id = b.participantId ?? '';
                const said = toMs(
                    spoken.filter((m) => m.senderId === id).at(-1)?.sendAt,
                );
                const busy = b.activity ?? '';
                const isMe = id === playerId;
                states.current.set(id, (): AvatarState | null => {
                    const pose = poseOf(
                        b,
                        scene,
                        place.id,
                        sceneOf,
                        walks.current,
                        shown.current,
                        slots,
                        serverNow(),
                    );
                    if (!pose) return null;
                    shown.current.set(id, pose.tile);
                    const walk = walks.current.get(id);
                    if (!pose.moving && walk && !walk.out)
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
                        talking: serverNow() - said < 3000,
                    };
                });
                const here =
                    (b.destPlaceId ?? b.placeId) === place.id ||
                    b.placeId === place.id;
                if (!here) return null;
                return (
                    <Person
                        key={id}
                        id={id}
                        look={stableLook(
                            lookOf(b.appearance, avatarColour(b.participantId)),
                        )}
                        isMe={isMe}
                        lying={!!b.asleep || b.posture === 'Lying'}
                        clickable={!!onBodyClick}
                        states={states.current}
                        click={click}
                    />
                );
            })}
        </>
    );
}

const looks = new Map<string, Look>();
function stableLook(look: Look): Look {
    const k = JSON.stringify(look);
    const hit = looks.get(k);
    if (hit) return hit;
    looks.set(k, look);
    return look;
}

const Person = memo(function Person({
    id,
    look,
    isMe,
    lying,
    clickable,
    states,
    click,
}: {
    id: string;
    look: Look;
    isMe: boolean;
    lying: boolean;
    clickable: boolean;
    states: Map<string, () => AvatarState | null>;
    click: RefObject<((participantId: string) => void) | undefined>;
}) {
    const state = useCallback(() => states.get(id)?.() ?? null, [states, id]);
    const onClick = useCallback(() => click.current?.(id), [click, id]);
    return (
        <Avatar
            look={look}
            isMe={isMe}
            state={state}
            onClick={clickable ? onClick : undefined}
        >
            <Anchor id={`body:${id}`} y={lying ? 0.95 : 1.72} />
        </Avatar>
    );
});

const BEACON = 'beacon';

function Beacon({ tile, ground }: { tile: Pt; ground: number }) {
    const ref = useRef<THREE.Mesh>(null);
    useFrame(({ clock }) => {
        const m = ref.current;
        if (!m) return;
        const k = (clock.elapsedTime * 0.8) % 1;
        m.scale.setScalar(0.7 + k * 0.6);
        beaconMat.opacity = 0.9 * (1 - k);
    });
    return (
        <group position={[tile.x + 0.5, ground + 0.03, tile.y + 0.5]}>
            <mesh
                ref={ref}
                rotation={[-Math.PI / 2, 0, 0]}
                geometry={beaconRing}
                material={beaconMat}
            />
            <Anchor id={BEACON} y={0.25} />
        </group>
    );
}

const NO_LINES: RealtimeChatMessage[] = [];

function useSpoken(roomId: string): RealtimeChatMessage[] {
    return useStoreWithEqualityFn(
        useRealtimeStore,
        (s) => {
            const g = s.chatGroups[roomId];
            if (!g) return NO_LINES;
            const live = new Set(g.activeGenerationMessageIds);
            return g.messages
                .filter(
                    (m) =>
                        m.sendAt && m.content?.trim() && !live.has(m.messageId),
                )
                .slice(-8);
        },
        sameLines,
    );
}

const sameLines = (a: RealtimeChatMessage[], b: RealtimeChatMessage[]) =>
    a.length === b.length &&
    a.every(
        (m, i) =>
            m.messageId === b[i].messageId &&
            m.content === b[i].content &&
            m.sendAt === b[i].sendAt,
    );

interface Overlay {
    anchors: Map<string, (v: THREE.Vector3) => boolean>;
    labels: Map<string, HTMLElement>;
}

const OverlayContext = createContext<Overlay | null>(null);

function Anchor({ id, y }: { id: string; y: number }) {
    const overlay = useContext(OverlayContext);
    const ref = useRef<THREE.Group>(null);
    useEffect(() => {
        const g = ref.current;
        if (!overlay || !g) return;
        overlay.anchors.set(id, (v) => {
            for (let o: THREE.Object3D | null = g; o; o = o.parent)
                if (!o.visible) return false;
            g.getWorldPosition(v);
            return true;
        });
        return () => {
            overlay.anchors.delete(id);
        };
    }, [overlay, id]);
    return <group ref={ref} position={[0, y, 0]} />;
}

function FurniAnchors({ props }: { props: PlacedProp[] }) {
    const overlay = useContext(OverlayContext);
    useEffect(() => {
        if (!overlay) return;
        const ids = props
            .filter((p) => p.furniId)
            .map((p) => {
                const id = `furni:${p.furniId}`;
                const x = p.at[0] + p.size[0] / 2;
                const z = p.at[1] + p.size[1] / 2;
                const y = 1.45 + (p.y ?? 0);
                overlay.anchors.set(id, (v) => {
                    v.set(x, y, z);
                    return true;
                });
                return id;
            });
        return () => {
            for (const id of ids) overlay.anchors.delete(id);
        };
    }, [overlay, props]);
    return null;
}

function Projector() {
    const overlay = useContext(OverlayContext);
    const v = useMemo(() => new THREE.Vector3(), []);
    useFrame(({ camera, size }) => {
        if (!overlay) return;
        for (const [id, el] of overlay.labels) {
            const at = overlay.anchors.get(id);
            if (!at?.(v)) {
                el.style.visibility = 'hidden';
                continue;
            }
            v.project(camera);
            const x = ((v.x + 1) / 2) * size.width;
            const y = ((1 - v.y) / 2) * size.height;
            el.style.visibility = 'visible';
            el.style.transform = `translate(${x.toFixed(1)}px, ${y.toFixed(1)}px) translate(-50%, -100%)`;
        }
    });
    return null;
}

function useBubbleClock(spoken: RealtimeChatMessage[]): number {
    const [, setTick] = useState(0);
    useEffect(() => {
        const now = serverNow();
        const due = spoken
            .map((m) => toMs(m.sendAt) + BUBBLE_MS)
            .filter((t) => t > now);
        if (due.length === 0) return;
        const t = setTimeout(
            () => setTick((n) => n + 1),
            Math.min(...due) - now + 20,
        );
        return () => clearTimeout(t);
    });
    return serverNow();
}

function useTrack(overlay: Overlay) {
    const refs = useRef(new Map<string, (el: HTMLDivElement | null) => void>());
    return (id: string) => {
        let r = refs.current.get(id);
        if (!r) {
            r = (el) => {
                if (el) overlay.labels.set(id, el);
                else overlay.labels.delete(id);
            };
            refs.current.set(id, r);
        }
        return r;
    };
}

function Labels({
    place,
    bodies,
    furni,
    playerId,
    allEars,
    overlay,
}: Props & { overlay: Overlay }) {
    const spoken = useSpoken(place.talkRoomId ?? '');
    const now = useBubbleClock(spoken);
    const recent = spoken.filter((m) => now - toMs(m.sendAt) < BUBBLE_MS);
    const ages = useRef(new Map<number, number>());
    const ageOf = (m: RealtimeChatMessage) => {
        let a = ages.current.get(m.messageId);
        if (a === undefined) {
            a = Math.max(0, now - toMs(m.sendAt));
            ages.current.set(m.messageId, a);
            if (ages.current.size > 64)
                ages.current.delete(ages.current.keys().next().value ?? 0);
        }
        return a;
    };
    const track = useTrack(overlay);
    const here = bodies.filter(
        (b) =>
            (b.destPlaceId ?? b.placeId) === place.id || b.placeId === place.id,
    );
    const me = bodies.find((b) => b.participantId === playerId);
    const coming =
        me && me.destPlaceId === place.id && me.placeId !== place.id
            ? me
            : undefined;
    return (
        <div className="pl-labels">
            {coming ? (
                <div ref={track(BEACON)} className="pl-label">
                    <span className="pl-name pl-name--me">
                        🚶 {(coming.name ?? '').split(' ')[0]} soon
                    </span>
                </div>
            ) : null}
            {here.map((b) => {
                const id = b.participantId ?? '';
                const isMe = id === playerId;
                const first = (b.name ?? '').split(' ')[0];
                const look = lookOf(
                    b.appearance,
                    avatarColour(b.participantId),
                );
                const busy = b.activity ?? '';
                const icon = b.asleep
                    ? '💤'
                    : busy.includes('laptop')
                      ? '💻'
                      : busy.includes('diary')
                        ? '📓'
                        : busy.includes('phone')
                          ? '📱'
                          : busy
                            ? '✋'
                            : '';
                return (
                    <div
                        key={id}
                        ref={track(`body:${id}`)}
                        className="pl-label"
                    >
                        {recent
                            .filter((m) => m.senderId === id)
                            .slice(-2)
                            .map((m) => {
                                const heard = canHear(m, playerId, allEars);
                                const volume = volumeOf(m);
                                const text = heard
                                    ? (m.content ?? '')
                                    : scribble(m.content);
                                return (
                                    <div
                                        key={m.messageId}
                                        className={`pl-bubble${volume === 'shout' ? ' pl-bubble--shout' : volume === 'whisper' ? ' pl-bubble--whisper' : ''}${heard ? '' : ' pl-bubble--muffled'}`}
                                        style={
                                            {
                                                '--pl-look': look.top,
                                                '--pl-age': `${-ageOf(m)}ms`,
                                            } as CSSProperties
                                        }
                                    >
                                        <b>{first}</b>
                                        {text.length > 120
                                            ? `${text.slice(0, 119)}…`
                                            : text}
                                    </div>
                                );
                            })}
                        <span
                            className={`pl-name${isMe ? ' pl-name--me' : ''}`}
                            style={{ '--pl-look': look.top } as CSSProperties}
                        >
                            <i />
                            {isMe ? `${first} ★` : first}
                            {icon ? ` ${icon}` : ''}
                        </span>
                    </div>
                );
            })}
            {furni
                .filter(
                    (f) =>
                        f.placeId === place.id &&
                        (f.notable || f.on || isDoable(f)),
                )
                .map((f) => (
                    <div
                        key={f.furniId}
                        ref={track(`furni:${f.furniId}`)}
                        className="pl-label"
                    >
                        {f.on && f.kind === 'jukebox' ? (
                            <span className="pl-notes">♪ ♫</span>
                        ) : null}
                        {f.notable ? (
                            <span className="pl-mark" title={f.notable}>
                                !
                            </span>
                        ) : isDoable(f) ? (
                            <span
                                className="pl-spark"
                                title={f.useLabel ?? undefined}
                            >
                                ✦
                            </span>
                        ) : null}
                    </div>
                ))}
        </div>
    );
}
