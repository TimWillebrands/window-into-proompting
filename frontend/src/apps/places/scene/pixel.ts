import { useFrame, useThree } from '@react-three/fiber';
import { useEffect, useMemo, useState } from 'react';
import * as THREE from 'three';
import {
    abs,
    dot,
    float,
    floor,
    fract,
    fwidth,
    getViewPosition,
    max,
    min,
    screenCoordinate,
    screenSize,
    screenUV,
    smoothstep,
    sqrt,
    texture,
    uniform,
    vec2,
    vec3,
    vec4,
} from 'three/tsl';
import {
    MeshBasicNodeMaterial,
    MeshNormalNodeMaterial,
    type Node,
    QuadMesh,
    type WebGPURenderer,
} from 'three/webgpu';

export const PIXEL_DPR = 0.5;
export const PIXEL_STYLE = { imageRendering: 'pixelated' } as const;

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

export interface PixelGrid {
    width: number;
    height: number;
    step: number;
    right: THREE.Vector3;
    up: THREE.Vector3;
    forward: THREE.Vector3;
}

type Gl = object;

const grids = new WeakMap<Gl, PixelGrid>();

export const pixelGrid = (gl: Gl): PixelGrid | null => grids.get(gl) ?? null;

const world = new THREE.Vector3();

export function snapToPixel(gl: Gl, o: THREE.Object3D) {
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

const OUTLINE = 0.45;
const RIM = 0.3;
const CREASE = 0.18;

function noDepth(o: THREE.Object3D): boolean {
    if ((o as THREE.Points).isPoints || (o as THREE.Line).isLine) return true;
    if ((o as THREE.Sprite).isSprite) return true;
    const m = (o as THREE.Mesh).material;
    if (!m) return false;
    return Array.isArray(m)
        ? m.some((x) => !x.depthWrite)
        : !(m as THREE.Material).depthWrite;
}

const unpack = (n: Node) => n.rgb.mul(2).sub(1).normalize();

const pass = (colorNode: Node) => {
    const m = new MeshBasicNodeMaterial({
        depthTest: false,
        depthWrite: false,
        toneMapped: false,
        transparent: true,
        blending: THREE.NoBlending,
    });
    m.colorNode = colorNode;
    return m;
};

class Pixelator {
    private color = new THREE.RenderTarget(1, 1, {
        type: THREE.HalfFloatType,
        minFilter: THREE.NearestFilter,
        magFilter: THREE.NearestFilter,
        depthTexture: new THREE.DepthTexture(1, 1),
    });
    private normal = new THREE.RenderTarget(1, 1, {
        minFilter: THREE.NearestFilter,
        magFilter: THREE.NearestFilter,
    });
    private edged = new THREE.RenderTarget(1, 1, {
        type: THREE.HalfFloatType,
        minFilter: THREE.LinearFilter,
        magFilter: THREE.LinearFilter,
    });
    private normalMat = new MeshNormalNodeMaterial();
    private size = uniform(new THREE.Vector2(1, 1));
    private invProj = uniform(new THREE.Matrix4());
    private origin = uniform(new THREE.Vector2());
    private step = uniform(1);
    private shift = uniform(new THREE.Vector2());
    private edgeMat = pass(this.edges());
    private upMat = pass(this.upscale());
    private quad = new QuadMesh(this.edgeMat);
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

    private edges(): Node {
        const colour = (q: Node) => texture(this.color.texture, q);
        const depth = (q: Node) =>
            texture(this.color.depthTexture as THREE.DepthTexture, q).r;
        const normal = (q: Node) => unpack(texture(this.normal.texture, q));
        const at = (q: Node, d: Node) => getViewPosition(q, d, this.invProj);
        const uv = screenUV;
        const c = colour(uv);
        const d = depth(uv);
        const px = vec2(1).div(this.size);
        const p = at(uv, d);
        const n = normal(uv);
        const size = at(uv.add(vec2(px.x, 0)), d)
            .sub(p)
            .length();
        const slope = min(
            sqrt(max(n.z.mul(n.z).oneMinus(), 0)).div(max(n.z, 0.05)),
            8,
        );
        const reach = size.mul(slope.add(1.5));
        let outline: Node = float(0).greaterThan(1);
        let rim: Node = outline;
        let crease: Node = outline;
        for (const o of [
            vec2(px.x, 0),
            vec2(px.x.negate(), 0),
            vec2(0, px.y),
            vec2(0, px.y.negate()),
        ]) {
            const q = uv.add(o);
            const dq = depth(q);
            const pq = at(q, dq);
            const nq = normal(q);
            const beyond = normal(q.add(o));
            const behind = p.z.sub(pq.z);
            const away = behind.greaterThan(reach);
            const corner = dq
                .lessThan(1)
                .and(abs(behind).lessThanEqual(reach))
                .and(dot(n, nq).oneMinus().greaterThanEqual(0.15))
                .and(dot(n.sub(nq), vec3(1)).greaterThan(0))
                .and(dot(nq, beyond).greaterThanEqual(0.9));
            const convex = dot(pq.sub(p), n).lessThan(0);
            outline = outline.or(away);
            rim = rim.or(away.not().and(corner).and(convex));
            crease = crease.or(away.not().and(corner).and(convex.not()));
        }
        const col = outline.select(
            c.rgb.mul(1 - OUTLINE),
            rim.select(
                c.rgb.mul(1 + RIM).add(c.a.mul(RIM * 0.03)),
                crease.select(c.rgb.mul(1 - CREASE), c.rgb),
            ),
        );
        return d.greaterThanEqual(1).select(c, vec4(col, c.a));
    }

    private upscale(): Node {
        const frag = vec2(
            screenCoordinate.x,
            screenSize.y.sub(screenCoordinate.y),
        );
        const v = frag.sub(this.origin).div(this.step).add(this.shift);
        const box = fwidth(v).clamp(1e-5, 1);
        const tx = v.sub(box.mul(0.5));
        const seam = smoothstep(box.oneMinus(), vec2(1), fract(tx));
        const at = floor(tx).add(0.5).add(seam).div(this.size);
        return texture(this.edged.texture, vec2(at.x, at.y.oneMinus()));
    }

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

    render(gl: WebGPURenderer, scene: THREE.Scene, camera: THREE.Camera) {
        gl.getDrawingBufferSize(this.buffer);
        const bw = this.buffer.x;
        const bh = this.buffer.y;
        const n = Math.max(1, Math.round(gl.getPixelRatio() / PIXEL_DPR));
        const w = Math.ceil(bw / n) + 2;
        const h = Math.ceil(bh / n) + 2;
        const shift = this.shift.value;
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

        this.size.value.set(w, h);
        this.invProj.value.copy(view.projectionMatrixInverse);
        this.quad.material = this.edgeMat;
        gl.setRenderTarget(this.edged);
        this.quad.render(gl);

        this.origin.value.set(
            Math.round((bw - w * n) / 2),
            Math.round((bh - h * n) / 2),
        );
        this.step.value = n;
        this.quad.material = this.upMat;
        gl.setRenderTarget(null);
        this.quad.render(gl);
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

export function Pixelate() {
    const gl = useThree((s) => s.gl);
    const pixelator = useMemo(() => new Pixelator(), []);
    useEffect(
        () => () => {
            grids.delete(gl);
            pixelator.dispose();
        },
        [gl, pixelator],
    );
    useFrame(
        ({ gl, scene, camera }) =>
            pixelator.render(gl as unknown as WebGPURenderer, scene, camera),
        1,
    );
    return null;
}
