import * as THREE from 'three';
import type { WebGPURenderer } from 'three/webgpu';
import type { Appearance } from '../../../api/model/appearance';
import { CLOTH, EYES, lookOf, SKIN } from '../iso/appearance';
import { avatarColour } from '../places-lib';
import { placesRenderer } from '../scene/gpu';
import { PIXEL_DPR } from '../scene/pixel';
import { SCENE_DETAIL } from './compile';
import type { FigureDoc } from './doc';
import { figureDoc } from './dress';
import { figureObject, setInk } from './object';
import { loadPrints } from './paint';
import { Puppet } from './puppet';
import { compiled, svgsOf } from './runtime';

const SHOWN = 72;
const GRID = SHOWN * PIXEL_DPR;
const SIZE = GRID * 4;
const VERSION = 2;
const SHELF = 'portraits';
const KEEP = 256;
const INK = 1.1;
const SPAN = 2.1;
const LIFT = 0.28;
const TURN = 0.3;
const FOV = 18;
const PALE = 0.62;

const STYLES = ['short', 'long', 'curly', 'bun', 'ponytail', 'bob', 'afro'];
const TOPS = ['tshirt', 'hoodie', 'shirt', 'sweater', 'jacket'];
const NATURAL_HAIR = ['black', 'brown', 'blonde', 'red', 'grey'];

export function seededAppearance(seed: string): Appearance {
    let h = 2166136261;
    for (let i = 0; i < seed.length; i++)
        h = Math.imul(h ^ seed.charCodeAt(i), 16777619);
    const pick = <T>(from: T[]) => {
        h = Math.imul(h ^ (h >>> 15), 2246822519);
        return from[(h >>> 0) % from.length];
    };
    return {
        skin: pick(Object.keys(SKIN)),
        hair: pick(NATURAL_HAIR),
        hairStyle: pick(STYLES),
        eyes: pick(Object.keys(EYES)),
        top: pick(Object.keys(CLOTH)),
        topStyle: pick(TOPS),
    } as Appearance;
}

const WHITE = new THREE.Color('#ffffff');

interface Stage {
    renderer: WebGPURenderer;
    scene: THREE.Scene;
    camera: THREE.PerspectiveCamera;
    picture: HTMLCanvasElement;
    pen: CanvasRenderingContext2D;
}

let stage: Promise<Stage> | null = null;

function theStage(): Promise<Stage> {
    stage ??= setStage();
    stage.catch(() => {
        stage = null;
    });
    return stage;
}

async function setStage(): Promise<Stage> {
    const renderer = await placesRenderer({ alpha: true, antialias: false });
    renderer.setPixelRatio(1);
    renderer.setSize(GRID, GRID, false);
    const picture = document.createElement('canvas');
    picture.width = picture.height = SIZE;
    const pen = picture.getContext('2d');
    if (!pen) throw new Error('no 2d canvas');
    pen.imageSmoothingEnabled = false;
    const scene = new THREE.Scene();
    scene.add(new THREE.AmbientLight('#fff1f6', 1.4));
    const key = new THREE.DirectionalLight('#fff6e8', 1.5);
    key.position.set(1.2, 3, 4);
    scene.add(key);
    return {
        renderer,
        scene,
        camera: new THREE.PerspectiveCamera(FOV, 1, 0.1, 20),
        picture,
        pen,
    };
}

const pngOf = (canvas: HTMLCanvasElement) =>
    new Promise<Blob>((resolve, reject) =>
        canvas.toBlob(
            (b) => (b ? resolve(b) : reject(new Error('no pixels'))),
            'image/png',
        ),
    );

async function draw(doc: FigureDoc): Promise<Blob> {
    const [c, images, { renderer, scene, camera, picture, pen }] =
        await Promise.all([
            compiled(doc, SCENE_DETAIL),
            loadPrints(svgsOf(doc)),
            theStage(),
        ]);
    const figure = figureObject(c, doc, { cloth: 256, face: 256, images });
    const puppet = new Puppet(figure, doc);
    try {
        puppet.play('stand', 0);
        puppet.update(0.4);
        figure.face(0, true);
        scene.add(figure.group);
        scene.updateMatrixWorld(true);
        const head = (figure.byName.get('Head') as THREE.Bone).getWorldPosition(
            new THREE.Vector3(),
        );
        const headH = c.H * THREE.MathUtils.lerp(0.215, 0.265, doc.body.head);
        head.y += headH * LIFT;
        const back =
            (headH * SPAN) / 2 / Math.tan(THREE.MathUtils.degToRad(FOV / 2));
        camera.position.set(
            head.x + Math.sin(TURN) * back,
            head.y,
            head.z + Math.cos(TURN) * back,
        );
        camera.lookAt(head);
        scene.background = new THREE.Color(doc.top.color).lerp(WHITE, PALE);
        setInk(INK, GRID, GRID);
        renderer.render(scene, camera);
        const frame = await createImageBitmap(await pngOf(renderer.domElement));
        pen.clearRect(0, 0, SIZE, SIZE);
        pen.drawImage(frame, 0, 0, SIZE, SIZE);
        frame.close();
        return await pngOf(picture);
    } finally {
        scene.remove(figure.group);
        puppet.dispose();
        figure.dispose();
    }
}

async function shelfKey(doc: string) {
    const sum = await crypto.subtle.digest(
        'SHA-256',
        new TextEncoder().encode(doc),
    );
    const hex = Array.from(new Uint8Array(sum), (b) =>
        b.toString(16).padStart(2, '0'),
    ).join('');
    return `/portraits/${VERSION}/${hex}`;
}

async function shelved(doc: FigureDoc, id: string): Promise<Blob> {
    const shelf = await caches.open(SHELF).catch(() => null);
    const key = shelf ? await shelfKey(id).catch(() => null) : null;
    if (!shelf || !key) return draw(doc);
    const hit = await shelf.match(key);
    if (hit) return hit.blob();
    const blob = await draw(doc);
    await shelf.put(key, new Response(blob));
    const all = await shelf.keys();
    await Promise.all(all.slice(0, -KEEP).map((k) => shelf.delete(k)));
    return blob;
}

const urls = new Map<string, Promise<string>>();

export function portrait(
    appearance: Appearance | null | undefined,
    seed: string,
): Promise<string> {
    const doc = figureDoc(
        lookOf(appearance ?? seededAppearance(seed), avatarColour(seed)),
    );
    const id = JSON.stringify(doc);
    const hit = urls.get(id);
    if (hit) return hit;
    const url = (
        typeof caches === 'undefined' ? draw(doc) : shelved(doc, id)
    ).then((b) => URL.createObjectURL(b));
    url.catch(() => urls.delete(id));
    urls.set(id, url);
    return url;
}
