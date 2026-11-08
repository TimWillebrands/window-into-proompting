import * as THREE from 'three';
import type { Appearance } from '../../../api/model/appearance';
import { CLOTH, EYES, lookOf, SKIN } from '../iso/appearance';
import { avatarColour } from '../places-lib';
import { PIXEL_DPR } from '../scene/pixel';
import { SCENE_DETAIL } from './compile';
import type { FigureDoc } from './doc';
import { figureDoc } from './dress';
import { figureObject, setInk } from './object';
import { loadPrints } from './paint';
import { Puppet } from './puppet';
import { compiled, svgsOf } from './runtime';

/** Display picture edge whose pixels match the scenes' (ADR 0023). */
const SHOWN = 72;
/** Pixels drawn across. */
const GRID = SHOWN * PIXEL_DPR;
/** Square edge of the image: the grid blown up unsmoothed, so every use is pixelated. */
const SIZE = GRID * 4;
/** Bump when framing or lighting changes: old pictures are thrown away. */
const VERSION = 2;
const SHELF = 'portraits';
const KEEP = 256;
const INK = 1.1;
/** Head heights the frame spans, and how far above the head bone it centres. */
const SPAN = 2.1;
const LIFT = 0.28;
const TURN = 0.3;
const FOV = 18;
/** How far a backdrop fades from their top toward white. */
const PALE = 0.62;

const STYLES = ['short', 'long', 'curly', 'bun', 'ponytail', 'bob', 'afro'];
const TOPS = ['tshirt', 'hoodie', 'shirt', 'sweater', 'jacket'];
const NATURAL_HAIR = ['black', 'brown', 'blonde', 'red', 'grey'];

/** A stable look for someone without a Body yet, from any string. */
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

let stage: {
    renderer: THREE.WebGLRenderer;
    scene: THREE.Scene;
    camera: THREE.PerspectiveCamera;
    picture: HTMLCanvasElement;
    pen: CanvasRenderingContext2D;
} | null = null;

/** One small GL context for every portrait, however many are on screen. */
function theStage() {
    if (stage) return stage;
    const renderer = new THREE.WebGLRenderer({
        alpha: true,
        antialias: false,
        preserveDrawingBuffer: true,
    });
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
    stage = {
        renderer,
        scene,
        camera: new THREE.PerspectiveCamera(FOV, 1, 0.1, 20),
        picture,
        pen,
    };
    return stage;
}

/** Head and shoulders, standing as they stand, smiling. */
async function draw(doc: FigureDoc): Promise<Blob> {
    const [c, images] = await Promise.all([
        compiled(doc, SCENE_DETAIL),
        loadPrints(svgsOf(doc)),
    ]);
    const { renderer, scene, camera, picture, pen } = theStage();
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
        pen.drawImage(renderer.domElement, 0, 0, SIZE, SIZE);
        return await new Promise<Blob>((resolve, reject) =>
            picture.toBlob(
                (b) => (b ? resolve(b) : reject(new Error('no pixels'))),
                'image/png',
            ),
        );
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

/** The picture from the browser's shelf, or drawn and shelved; a shelf that won't open just means drawing. */
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

/** A picture of someone's Figure as an object URL; the key is the doc, so a change of clothes is a new picture. */
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
