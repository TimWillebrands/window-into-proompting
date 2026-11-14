import { useFrame, useThree } from '@react-three/fiber';
import { useEffect, useMemo, useState } from 'react';
import * as THREE from 'three';

/** The house look (ADR 0023): one pixel per two CSS pixels, drawn low-res and scaled up by `Pixelate`. */
export const PIXEL_DPR = 0.5;
export const PIXEL_STYLE = { imageRendering: 'pixelated' } as const;

/** Light pools fall off in whole stops and spot cones in thirds, so lamps draw rings, not mush. */
function stepLights() {
    const c = THREE.ShaderChunk;
    c.lights_pars_begin = c.lights_pars_begin
        .replace(
            'return distanceFalloff;',
            'return distanceFalloff <= 0.0 ? 0.0 : exp2( floor( log2( distanceFalloff ) + 0.5 ) );',
        )
        .replace(
            'return smoothstep( coneCosine, penumbraCosine, angleCosine );',
            'return floor( smoothstep( coneCosine, penumbraCosine, angleCosine ) * 3.0 + 0.5 ) / 3.0;',
        );
}
stepLights();

/** The canvas's device pixel ratio, kept current across browser zoom and screen moves. */
export function usePixelDpr(): number {
    const [dpr, setDpr] = useState(() => window.devicePixelRatio || 1);
    useEffect(() => {
        const mq = window.matchMedia(`(resolution: ${dpr}dppx)`);
        const changed = () => setDpr(window.devicePixelRatio || 1);
        mq.addEventListener('change', changed);
        return () => mq.removeEventListener('change', changed);
    }, [dpr]);
    return dpr;
}

/** The pixel grid a canvas last drew on. */
export interface PixelGrid {
    width: number;
    height: number;
    /** World units per pixel; 0 under a perspective camera, where nothing snaps. */
    step: number;
    right: THREE.Vector3;
    up: THREE.Vector3;
    forward: THREE.Vector3;
}

const grids = new WeakMap<THREE.WebGLRenderer, PixelGrid>();

export const pixelGrid = (gl: THREE.WebGLRenderer): PixelGrid | null =>
    grids.get(gl) ?? null;

const world = new THREE.Vector3();

/** Puts an object's origin on a whole pixel, sliding along the view so its height stays. */
export function snapToPixel(gl: THREE.WebGLRenderer, o: THREE.Object3D) {
    const g = grids.get(gl);
    if (!g?.step || Math.abs(g.forward.y) < 0.1) return;
    world.copy(o.position);
    o.parent?.localToWorld(world);
    const r = world.dot(g.right) / g.step;
    const u = world.dot(g.up) / g.step;
    const dr = (Math.round(r) - r) * g.step;
    const du = (Math.round(u) - u) * g.step;
    const along = -(dr * g.right.y + du * g.up.y) / g.forward.y;
    world
        .addScaledVector(g.right, dr)
        .addScaledVector(g.up, du)
        .addScaledVector(g.forward, along);
    o.parent?.worldToLocal(world);
    o.position.copy(world);
}

const QUAD_VERT = /* glsl */ `
varying vec2 vUv;
void main() {
    vUv = uv;
    gl_Position = vec4(position.xy, 0.0, 1.0);
}`;

/** Dark outlines where the far side drops away, a lit rim on convex corners, a shade in creases. */
const EDGE_FRAG = /* glsl */ `
uniform sampler2D tColor;
uniform sampler2D tDepth;
uniform sampler2D tNormal;
uniform vec2 uSize;
uniform mat4 uInvProj;
uniform float uOutline;
uniform float uRim;
uniform float uCrease;
varying vec2 vUv;

vec3 viewAt(vec2 uv, float d) {
    vec4 p = uInvProj * vec4(uv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    return p.xyz / p.w;
}

void look(vec2 o, vec3 p, vec3 n, float reach, inout float outline, inout float rim, inout float crease) {
    vec2 q = vUv + o;
    float dq = texture2D(tDepth, q).r;
    vec3 pq = viewAt(q, dq);
    float behind = p.z - pq.z;
    if (behind > reach) { outline = 1.0; return; }
    if (dq >= 1.0 || abs(behind) > reach) return;
    vec3 nq = normalize(texture2D(tNormal, q).rgb * 2.0 - 1.0);
    // One side of the seam only, the one facing up and right, so lines stay one pixel wide.
    if (1.0 - dot(n, nq) < 0.15 || dot(n - nq, vec3(1.0)) <= 0.0) return;
    // A face under two pixels wide (a floor seam's side) is detail, not a corner.
    vec3 beyond = normalize(texture2D(tNormal, q + o).rgb * 2.0 - 1.0);
    if (dot(nq, beyond) < 0.9) return;
    if (dot(pq - p, n) < 0.0) rim = 1.0; else crease = 1.0;
}

void main() {
    vec4 c = texture2D(tColor, vUv);
    float d = texture2D(tDepth, vUv).r;
    if (d >= 1.0) { gl_FragColor = c; return; }
    vec2 px = 1.0 / uSize;
    vec3 p = viewAt(vUv, d);
    vec3 n = normalize(texture2D(tNormal, vUv).rgb * 2.0 - 1.0);
    float size = length(viewAt(vUv + vec2(px.x, 0.0), d) - p);
    // A surface seen edge-on steps far between pixels without being an edge.
    float slope = min(sqrt(max(1.0 - n.z * n.z, 0.0)) / max(n.z, 0.05), 8.0);
    float reach = size * (1.5 + slope);
    float outline = 0.0;
    float rim = 0.0;
    float crease = 0.0;
    look(vec2(px.x, 0.0), p, n, reach, outline, rim, crease);
    look(vec2(-px.x, 0.0), p, n, reach, outline, rim, crease);
    look(vec2(0.0, px.y), p, n, reach, outline, rim, crease);
    look(vec2(0.0, -px.y), p, n, reach, outline, rim, crease);
    vec3 col = c.rgb;
    if (outline > 0.0) col *= 1.0 - uOutline;
    else if (rim > 0.0) col = col * (1.0 + uRim) + uRim * 0.03 * c.a;
    else if (crease > 0.0) col *= 1.0 - uCrease;
    gl_FragColor = vec4(col, c.a);
}`;

/** Whole pixels, blended only across the one screen pixel where two meet, so any scale and sub-pixel shift stays crisp. */
const UP_FRAG = /* glsl */ `
uniform sampler2D tPixels;
uniform vec2 uSize;
uniform vec2 uOrigin;
uniform float uStep;
uniform vec2 uShift;
void main() {
    vec2 v = (gl_FragCoord.xy - uOrigin) / uStep + uShift;
    vec2 box = clamp(fwidth(v), 1e-5, 1.0);
    vec2 tx = v - 0.5 * box;
    vec2 seam = smoothstep(1.0 - box, vec2(1.0), fract(tx));
    gl_FragColor = texture2D(tPixels, (floor(tx) + 0.5 + seam) / uSize);
    #include <colorspace_fragment>
}`;

const OUTLINE = 0.45;
const RIM = 0.3;
const CREASE = 0.18;

/** Skipped by the normal pass: what writes no depth would disagree with the depth it's read against. */
function noDepth(o: THREE.Object3D): boolean {
    if ((o as THREE.Points).isPoints || (o as THREE.Line).isLine) return true;
    if ((o as THREE.Sprite).isSprite) return true;
    const m = (o as THREE.Mesh).material;
    if (!m) return false;
    return Array.isArray(m)
        ? m.some((x) => !x.depthWrite)
        : !(m as THREE.Material).depthWrite;
}

class Pixelator {
    private color = new THREE.WebGLRenderTarget(1, 1, {
        type: THREE.HalfFloatType,
        minFilter: THREE.NearestFilter,
        magFilter: THREE.NearestFilter,
        depthTexture: new THREE.DepthTexture(1, 1),
    });
    private normal = new THREE.WebGLRenderTarget(1, 1, {
        minFilter: THREE.NearestFilter,
        magFilter: THREE.NearestFilter,
    });
    private edged = new THREE.WebGLRenderTarget(1, 1, {
        type: THREE.HalfFloatType,
        minFilter: THREE.LinearFilter,
        magFilter: THREE.LinearFilter,
    });
    private normalMat = new THREE.MeshNormalMaterial();
    private edgeMat = new THREE.ShaderMaterial({
        vertexShader: QUAD_VERT,
        fragmentShader: EDGE_FRAG,
        uniforms: {
            tColor: { value: this.color.texture },
            tDepth: { value: this.color.depthTexture },
            tNormal: { value: this.normal.texture },
            uSize: { value: new THREE.Vector2() },
            uInvProj: { value: new THREE.Matrix4() },
            uOutline: { value: OUTLINE },
            uRim: { value: RIM },
            uCrease: { value: CREASE },
        },
        depthTest: false,
        depthWrite: false,
        toneMapped: false,
    });
    private upMat = new THREE.ShaderMaterial({
        vertexShader: QUAD_VERT,
        fragmentShader: UP_FRAG,
        uniforms: {
            tPixels: { value: this.edged.texture },
            uSize: { value: new THREE.Vector2() },
            uOrigin: { value: new THREE.Vector2() },
            uStep: { value: 1 },
            uShift: { value: new THREE.Vector2() },
        },
        depthTest: false,
        depthWrite: false,
        toneMapped: false,
    });
    private quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), this.edgeMat);
    private quadScene = new THREE.Scene().add(this.quad);
    private quadCam = new THREE.Camera();
    private ortho = new THREE.OrthographicCamera();
    private persp = new THREE.PerspectiveCamera();
    private buffer = new THREE.Vector2();
    private eye = new THREE.Vector3();
    private hidden: THREE.Object3D[] = [];
    private grid: PixelGrid = {
        width: 1,
        height: 1,
        step: 0,
        right: new THREE.Vector3(),
        up: new THREE.Vector3(),
        forward: new THREE.Vector3(),
    };

    constructor() {
        this.quad.frustumCulled = false;
    }

    /** The low-res camera: the real one widened by the margin, and for ortho moved onto whole pixels. */
    private view(
        camera: THREE.Camera,
        w: number,
        h: number,
        bw: number,
        bh: number,
        n: number,
        shift: THREE.Vector2,
    ): THREE.Camera | null {
        const g = this.grid;
        shift.set(0, 0);
        g.step = 0;
        camera.updateMatrixWorld();
        g.right.setFromMatrixColumn(camera.matrixWorld, 0).normalize();
        g.up.setFromMatrixColumn(camera.matrixWorld, 1).normalize();
        g.forward
            .setFromMatrixColumn(camera.matrixWorld, 2)
            .negate()
            .normalize();
        const at = camera.getWorldPosition(this.eye);
        if ((camera as THREE.OrthographicCamera).isOrthographicCamera) {
            const c = camera as THREE.OrthographicCamera;
            const sx = ((c.right - c.left) / c.zoom / bw) * n;
            const sy = ((c.top - c.bottom) / c.zoom / bh) * n;
            const px = at.dot(g.right) / sx;
            const py = at.dot(g.up) / sy;
            shift.set(px - Math.round(px), py - Math.round(py));
            const o = this.ortho;
            o.left = (-w * sx) / 2;
            o.right = (w * sx) / 2;
            o.top = (h * sy) / 2;
            o.bottom = (-h * sy) / 2;
            o.near = c.near;
            o.far = c.far;
            o.zoom = 1;
            o.updateProjectionMatrix();
            o.position
                .copy(at)
                .addScaledVector(g.right, -shift.x * sx)
                .addScaledVector(g.up, -shift.y * sy);
            camera.getWorldQuaternion(o.quaternion);
            o.updateMatrixWorld();
            g.step = sx;
            return o;
        }
        if ((camera as THREE.PerspectiveCamera).isPerspectiveCamera) {
            const c = camera as THREE.PerspectiveCamera;
            const p = this.persp;
            const half = Math.tan(THREE.MathUtils.degToRad(c.fov / 2));
            p.fov = THREE.MathUtils.radToDeg(
                2 * Math.atan((half * h * n) / bh),
            );
            p.aspect = w / h;
            p.near = c.near;
            p.far = c.far;
            p.zoom = c.zoom;
            p.updateProjectionMatrix();
            p.position.copy(at);
            camera.getWorldQuaternion(p.quaternion);
            p.updateMatrixWorld();
            return p;
        }
        return null;
    }

    render(gl: THREE.WebGLRenderer, scene: THREE.Scene, camera: THREE.Camera) {
        gl.getDrawingBufferSize(this.buffer);
        const bw = this.buffer.x;
        const bh = this.buffer.y;
        // Whole screen pixels per pixel, so none come out wider than the rest.
        const n = Math.max(1, Math.round(gl.getPixelRatio() / PIXEL_DPR));
        const w = Math.ceil(bw / n) + 2;
        const h = Math.ceil(bh / n) + 2;
        const shift = this.upMat.uniforms.uShift.value as THREE.Vector2;
        const view = this.view(camera, w, h, bw, bh, n, shift);
        if (!view) {
            gl.render(scene, camera);
            return;
        }
        if (this.color.width !== w || this.color.height !== h) {
            this.color.setSize(w, h);
            this.normal.setSize(w, h);
            this.edged.setSize(w, h);
        }
        this.grid.width = w;
        this.grid.height = h;
        grids.set(gl, this.grid);

        gl.shadowMap.needsUpdate = true;
        gl.setRenderTarget(this.color);
        gl.render(scene, view);

        scene.traverseVisible((o) => {
            if (noDepth(o)) {
                o.visible = false;
                this.hidden.push(o);
            }
        });
        const background = scene.background;
        const override = scene.overrideMaterial;
        scene.background = null;
        scene.overrideMaterial = this.normalMat;
        gl.setRenderTarget(this.normal);
        gl.render(scene, view);
        scene.overrideMaterial = override;
        scene.background = background;
        for (const o of this.hidden) o.visible = true;
        this.hidden.length = 0;

        const e = this.edgeMat.uniforms;
        (e.uSize.value as THREE.Vector2).set(w, h);
        (e.uInvProj.value as THREE.Matrix4).copy(view.projectionMatrixInverse);
        this.quad.material = this.edgeMat;
        gl.setRenderTarget(this.edged);
        gl.render(this.quadScene, this.quadCam);

        const u = this.upMat.uniforms;
        (u.uSize.value as THREE.Vector2).set(w, h);
        // Where the low-res frame's corner lands, rounded so a still camera has seams on screen-pixel edges.
        (u.uOrigin.value as THREE.Vector2).set(
            Math.round((bw - w * n) / 2),
            Math.round((bh - h * n) / 2),
        );
        u.uStep.value = n;
        this.quad.material = this.upMat;
        gl.setRenderTarget(null);
        gl.render(this.quadScene, this.quadCam);
    }

    dispose() {
        this.color.depthTexture?.dispose();
        this.color.dispose();
        this.normal.dispose();
        this.edged.dispose();
        this.normalMat.dispose();
        this.edgeMat.dispose();
        this.upMat.dispose();
        this.quad.geometry.dispose();
    }
}

/** Takes over a canvas's drawing for the pixel look: low-res, edged, snapped, scaled up crisp. Use with `usePixelDpr`. */
export function Pixelate() {
    const gl = useThree((s) => s.gl);
    const pixelator = useMemo(() => new Pixelator(), []);
    useEffect(() => {
        const auto = gl.shadowMap.autoUpdate;
        // Shadows draw once per frame, not again for the normal pass.
        gl.shadowMap.autoUpdate = false;
        return () => {
            gl.shadowMap.autoUpdate = auto;
            grids.delete(gl);
            pixelator.dispose();
        };
    }, [gl, pixelator]);
    useFrame(({ gl, scene, camera }) => pixelator.render(gl, scene, camera), 1);
    return null;
}
