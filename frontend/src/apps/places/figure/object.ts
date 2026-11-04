import * as THREE from 'three';
import type { Compiled, Layer } from './compile';
import type { FigureDoc } from './doc';
import { drawFace, type FaceFrame } from './face';
import { type Images, paintBottom, paintLegwear, paintTop } from './paint';
import { BONES } from './skeleton';

const ramp = (() => {
    const steps = new Uint8Array([118, 186, 255, 255]);
    const t = new THREE.DataTexture(steps, 4, 1, THREE.RedFormat);
    t.minFilter = THREE.LinearFilter;
    t.magFilter = THREE.LinearFilter;
    t.needsUpdate = true;
    return t;
})();

/** Toon shading with a soft rim and a painted-on gloss, so bodies lift off busy floors. */
function toon(
    params: THREE.MeshToonMaterialParameters,
    shine: number,
): THREE.MeshToonMaterial {
    const m = new THREE.MeshToonMaterial(params);
    m.onBeforeCompile = (shader) => {
        shader.fragmentShader = shader.fragmentShader.replace(
            '#include <opaque_fragment>',
            `vec3 fN = normalize(normal);
            vec3 fV = normalize(vViewPosition);
            float rim = pow(1.0 - saturate(dot(fN, fV)), 3.0);
            outgoingLight += diffuseColor.rgb * rim * 0.3;
            float gloss = smoothstep(0.55, 0.85, pow(saturate(dot(fN, normalize(fV + vec3(-0.35, 0.65, 0.2)))), 14.0));
            outgoingLight += (diffuseColor.rgb * 0.5 + 0.09) * gloss * ${shine.toFixed(2)};
            // Black clothes keep their folds: lift the deepest shadows a little.
            outgoingLight = max(outgoingLight, vec3(0.018, 0.016, 0.024) + outgoingLight * 0.9);
            #include <opaque_fragment>`,
        );
    };
    m.customProgramCacheKey = () => `figure-toon-${shine}`;
    return m;
}

const inkUniforms = {
    /** Line width in framebuffer pixels over the framebuffer size. */
    uInk: { value: new THREE.Vector2(1.4 / 800, 1.4 / 600) },
};

/** Sets the ink line for the current canvas: constant on screen at any zoom. */
export function setInk(pixels: number, width: number, height: number) {
    inkUniforms.uInk.value.set(pixels / width, pixels / height);
}

/** The mesh again, inside out and pushed along its screen normals. */
const ink = (() => {
    const m = new THREE.MeshBasicMaterial({
        color: '#1b1420',
        side: THREE.BackSide,
    });
    m.onBeforeCompile = (shader) => {
        shader.uniforms.uInk = inkUniforms.uInk;
        shader.vertexShader = shader.vertexShader
            .replace(
                '#include <common>',
                'uniform vec2 uInk;\n#include <common>',
            )
            .replace(
                '#include <project_vertex>',
                `#include <project_vertex>
                vec2 inkN = normalize((projectionMatrix * vec4(transformedNormal, 0.0)).xy + 1e-5);
                gl_Position.xy += inkN * uInk * 2.0 * gl_Position.w;
                // Behind everything: on bumpy cloth the hull would otherwise speckle through at grazing angles.
                gl_Position.z += 0.0003 * gl_Position.w;`,
            );
    };
    return m;
})();

/** Atlas columns: open, blink, talk a little, talk wide. Rows: neutral, smiling. */
const FACE_COLS = 4;
const FACE_ROWS = 2;
const FRAMES: FaceFrame[] = [
    { open: 1, talk: 0, smile: 0 },
    { open: 0, talk: 0, smile: 0 },
    { open: 1, talk: 0.45, smile: 0 },
    { open: 1, talk: 1, smile: 0 },
];

function faceAtlas(
    doc: FigureDoc,
    cell: number,
    images: Images,
): THREE.CanvasTexture {
    const canvas = document.createElement('canvas');
    canvas.width = cell * FACE_COLS;
    canvas.height = cell * FACE_ROWS;
    const ctx = canvas.getContext('2d') as CanvasRenderingContext2D;
    for (let row = 0; row < FACE_ROWS; row++)
        FRAMES.forEach((f, col) => {
            ctx.save();
            ctx.beginPath();
            ctx.rect(col * cell, row * cell, cell, cell);
            ctx.clip();
            ctx.translate(col * cell, row * cell);
            drawFace(
                ctx,
                doc.face,
                doc.body.skin,
                cell,
                { ...f, smile: row ? 0.8 : 0 },
                doc.face.paint ? images.get(doc.face.paint) : undefined,
            );
            ctx.restore();
        });
    const t = new THREE.CanvasTexture(canvas);
    t.colorSpace = THREE.SRGBColorSpace;
    t.anisotropy = 4;
    t.repeat.set(1 / FACE_COLS, 1 / FACE_ROWS);
    return t;
}

function clothAtlas(
    layer: Layer,
    c: Compiled,
    doc: FigureDoc,
    images: Images,
    size: number,
): THREE.CanvasTexture {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = size;
    const ctx = canvas.getContext('2d', {
        willReadFrequently: false,
    }) as CanvasRenderingContext2D;
    const info = {
        frame: layer.frame as NonNullable<Layer['frame']>,
        chestY: c.chestY,
        bellyY: c.bellyY,
        H: c.H,
    };
    if (layer.paint === 'bottom') paintBottom(ctx, doc.bottom, info, images);
    else if (layer.paint === 'legwear' && doc.legwear)
        paintLegwear(ctx, doc.legwear);
    else {
        const top =
            (layer.paint === 'outer'
                ? doc.outer
                : layer.paint === 'under'
                  ? doc.under
                  : doc.top) ?? doc.top;
        paintTop(ctx, top, info, images);
    }
    const t = new THREE.CanvasTexture(canvas);
    t.colorSpace = THREE.SRGBColorSpace;
    t.anisotropy = 4;
    return t;
}

export interface FigureObject {
    group: THREE.Group;
    bones: THREE.Bone[];
    byName: Map<string, THREE.Bone>;
    pelvisScale: number;
    /** Height the soles add. */
    lift: number;
    /** Picks the face: column of the atlas and whether they're smiling. */
    face(col: number, smiling: boolean): void;
    dispose(): void;
}

const SHINES = [0.04, 0.12, 0.7];
const shineOf = (layer: Layer) =>
    layer.shine ??
    (layer.kind === 'skin' || layer.kind === 'head' ? SHINES[1] : SHINES[0]);

/** One skinned mesh per shader variant, to compile before anyone walks in. */
export function warmupMeshes(): THREE.Group {
    const group = new THREE.Group();
    const bone = new THREE.Bone();
    group.add(bone);
    const skeleton = new THREE.Skeleton([bone]);
    const g = new THREE.BoxGeometry(1, 1, 1);
    const n = g.attributes.position.count;
    g.setAttribute(
        'skinIndex',
        new THREE.BufferAttribute(new Uint16Array(n * 4), 4),
    );
    g.setAttribute(
        'skinWeight',
        new THREE.BufferAttribute(
            new Float32Array(n * 4).map((_, i) => (i % 4 ? 0 : 1)),
            4,
        ),
    );
    g.setAttribute(
        'color',
        new THREE.BufferAttribute(new Float32Array(n * 3).fill(1), 3),
    );
    const map = new THREE.DataTexture(
        new Uint8Array([255, 255, 255, 255]),
        1,
        1,
    );
    const materials = [
        ...SHINES.map((s) =>
            toon({ vertexColors: true, gradientMap: ramp }, s),
        ),
        ...SHINES.map((s) =>
            toon({ vertexColors: true, gradientMap: ramp, map }, s),
        ),
        ink,
    ];
    for (const m of materials) {
        const mesh = new THREE.SkinnedMesh(g, m);
        mesh.frustumCulled = false;
        group.add(mesh);
        mesh.bind(skeleton);
    }
    return group;
}

export interface FigureOptions {
    /** Texture size for each garment. */
    cloth: number;
    /** Face cell size. */
    face: number;
    images: Images;
}

/** Skinned meshes on one shared skeleton, from compiled buffers. */
export function figureObject(
    c: Compiled,
    doc: FigureDoc,
    o: FigureOptions,
): FigureObject {
    const group = new THREE.Group();
    const bones = BONES.map((b, i) => {
        const bone = new THREE.Bone();
        bone.name = b.name;
        bone.position.fromArray(c.bones[i].t);
        bone.quaternion.fromArray(c.bones[i].q);
        return bone;
    });
    BONES.forEach((b, i) => {
        if (b.parent >= 0) bones[b.parent].add(bones[i]);
        else group.add(bones[i]);
    });
    group.updateMatrixWorld(true);
    const skeleton = new THREE.Skeleton(bones);
    const owned: { dispose(): void }[] = [skeleton];
    let faceMap: THREE.Texture | null = null;
    for (const layer of c.layers) {
        const g = new THREE.BufferGeometry();
        g.setAttribute(
            'position',
            new THREE.BufferAttribute(layer.positions, 3),
        );
        g.setAttribute('normal', new THREE.BufferAttribute(layer.normals, 3));
        g.setAttribute(
            'skinIndex',
            new THREE.BufferAttribute(layer.skinIndex, 4),
        );
        g.setAttribute(
            'skinWeight',
            new THREE.BufferAttribute(layer.skinWeight, 4),
        );
        g.setAttribute('color', new THREE.BufferAttribute(layer.colors, 3));
        if (layer.uvs)
            g.setAttribute('uv', new THREE.BufferAttribute(layer.uvs, 2));
        g.setIndex(new THREE.BufferAttribute(layer.indices, 1));
        const m = toon(
            { color: layer.color, vertexColors: true, gradientMap: ramp },
            shineOf(layer),
        );
        if (layer.kind === 'head') {
            faceMap = faceAtlas(doc, o.face, o.images);
            m.map = faceMap;
            m.color.set('#ffffff');
        } else if (layer.paint)
            m.map = clothAtlas(layer, c, doc, o.images, o.cloth);
        if (layer.holes) {
            m.alphaTest = 0.5;
            m.side = THREE.DoubleSide;
        } else if (layer.sheer) {
            m.transparent = true;
            m.opacity = layer.opacity ?? 1;
            m.depthWrite = false;
        }
        if (m.map) owned.push(m.map);
        owned.push(g, m);
        const mesh = new THREE.SkinnedMesh(g, m);
        mesh.name = layer.name;
        mesh.castShadow = true;
        mesh.frustumCulled = false;
        group.add(mesh);
        mesh.bind(skeleton, new THREE.Matrix4());
        if (!layer.sheer) {
            const line = new THREE.SkinnedMesh(g, ink);
            line.name = `${layer.name}-ink`;
            line.frustumCulled = false;
            group.add(line);
            line.bind(skeleton, new THREE.Matrix4());
        }
    }
    // The mixer's "original state" is the clips' T rest, not the bind pose.
    BONES.forEach((b, i) => {
        bones[i].quaternion.fromArray(b.q);
        if (b.name === 'pelvis') bones[i].position.fromArray(b.t);
    });
    return {
        group,
        bones,
        byName: new Map(bones.map((b) => [b.name, b])),
        pelvisScale: c.pelvisScale,
        lift: c.lift,
        face(col, smiling) {
            faceMap?.offset.set(col / FACE_COLS, smiling ? 0 : 1 / FACE_ROWS);
        },
        dispose() {
            for (const o2 of owned) o2.dispose();
        },
    };
}
