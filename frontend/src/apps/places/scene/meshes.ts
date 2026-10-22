import { useSyncExternalStore } from 'react';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

/** One material's worth of a catalogue model, transforms baked, bottom-centre at the origin. */
export interface MeshGroup {
    material: string;
    colour: string;
    emissive: boolean;
    geometry: THREE.BufferGeometry;
}

/** A catalogue model (ADR 0027) normalised for the prefab pipeline: grouped by material, in model units. */
export interface CatalogueMesh {
    groups: MeshGroup[];
    size: THREE.Vector3;
}

const meshes = new Map<string, CatalogueMesh | 'loading' | 'failed'>();
const listeners = new Set<() => void>();
let version = 0;

const bump = () => {
    version++;
    for (const l of listeners) l();
};

/** The model if loaded; starts loading it otherwise. */
export function meshNow(url: string): CatalogueMesh | undefined {
    const m = meshes.get(url);
    if (m === undefined) {
        meshes.set(url, 'loading');
        new GLTFLoader()
            .loadAsync(url)
            .then((gltf) => meshes.set(url, normalise(gltf.scene)))
            .catch(() => meshes.set(url, 'failed'))
            .finally(bump);
        return undefined;
    }
    return typeof m === 'string' ? undefined : m;
}

/** Bumps when a model lands, so builds that skipped it run again. */
export const useMeshVersion = () =>
    useSyncExternalStore(
        (l) => {
            listeners.add(l);
            return () => listeners.delete(l);
        },
        () => version,
    );

/** Bake world transforms, split by material, flat-shade, and sit the model on the floor at the origin. */
export function normalise(root: THREE.Object3D): CatalogueMesh {
    root.updateMatrixWorld(true);
    const byMaterial = new Map<
        string,
        { colour: string; emissive: boolean; parts: THREE.BufferGeometry[] }
    >();
    root.traverse((o) => {
        const mesh = o as THREE.Mesh;
        if (!mesh.isMesh) return;
        const mats = Array.isArray(mesh.material)
            ? mesh.material
            : [mesh.material];
        const g = mesh.geometry.clone().applyMatrix4(mesh.matrixWorld);
        const flat = g.index ? g.toNonIndexed() : g;
        const ranges = flat.groups.length
            ? flat.groups
            : [
                  {
                      start: 0,
                      count: flat.getAttribute('position').count,
                      materialIndex: 0,
                  },
              ];
        for (const r of ranges) {
            const mat = mats[
                r.materialIndex ?? 0
            ] as THREE.MeshStandardMaterial;
            const name = mat?.name || 'default';
            const entry = byMaterial.get(name) ?? {
                colour: `#${(mat?.color ?? new THREE.Color('#888')).getHexString()}`,
                emissive: !!mat?.emissive && mat.emissive.getHex() !== 0,
                parts: [],
            };
            entry.parts.push(slice(flat, r.start, r.count));
            byMaterial.set(name, entry);
        }
    });
    const groups: MeshGroup[] = [...byMaterial].map(([material, e]) => ({
        material,
        colour: e.colour,
        emissive: e.emissive,
        geometry: mergeGeometries(e.parts) ?? new THREE.BufferGeometry(),
    }));
    const box = new THREE.Box3();
    for (const g of groups) {
        g.geometry.computeBoundingBox();
        if (g.geometry.boundingBox) box.union(g.geometry.boundingBox);
    }
    const centre = box.getCenter(new THREE.Vector3());
    for (const g of groups)
        g.geometry.translate(-centre.x, -box.min.y, -centre.z);
    return { groups, size: box.getSize(new THREE.Vector3()) };
}

/** Positions and normals of vertices [start, start + count) of a non-indexed geometry. */
function slice(g: THREE.BufferGeometry, start: number, count: number) {
    const out = new THREE.BufferGeometry();
    for (const name of ['position', 'normal']) {
        const a = g.getAttribute(name) as THREE.BufferAttribute | undefined;
        if (!a) continue;
        out.setAttribute(
            name,
            new THREE.BufferAttribute(
                (a.array as Float32Array).slice(
                    start * a.itemSize,
                    (start + count) * a.itemSize,
                ),
                a.itemSize,
            ),
        );
    }
    if (!out.getAttribute('normal')) out.computeVertexNormals();
    return out;
}

/** Uniform scale that fits a model inside [w, h, d] tiles; h 0 = only the footprint counts. */
export function fitScale(size: THREE.Vector3, fit: [number, number, number]) {
    const [w, h, d] = fit;
    const s = Math.min(
        size.x > 0 ? w / size.x : Infinity,
        size.z > 0 ? d / size.z : Infinity,
        h > 0 && size.y > 0 ? h / size.y : Infinity,
    );
    return Number.isFinite(s) ? s : 1;
}
