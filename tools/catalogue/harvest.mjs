#!/usr/bin/env node
// Catalogue pipeline (ADR 0027, issue 06): curate CC0 models from Poly Pizza up front, so runtime only picks names.
//   POLY_PIZZA_KEY=… node tools/catalogue/harvest.mjs search   → shortlist.json + shortlist.html to review
//   POLY_PIZZA_KEY=… node tools/catalogue/harvest.mjs fetch    → downloads picks.json, generates the prefab entries
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '../..');
const MODELS = join(ROOT, 'frontend/public/models');
const GENERATED = join(ROOT, 'frontend/src/apps/places/scene/catalogue.gen.ts');
const BACKEND = join(ROOT, 'backend/World/Places/prefabs.json');
const API = 'https://api.poly.pizza/v1.1';
const CREATORS = { Kenney: 'kenney', Quaternius: 'quaternius', 'Kay Lousberg': 'kay-lousberg' };
const PREFIX = { kenney: 'k-', quaternius: 'q-', 'kay-lousberg': 'kl-' };
/** Real-world metres per model unit; furniture kits are modelled small. Buildings fit their footprint instead. */
const UNIT = { kenney: 2, quaternius: 1, 'kay-lousberg': 1 };
const MAX_TRIS = 3000;

const key = process.env.POLY_PIZZA_KEY;
const api = async (path) => {
    if (!key) throw new Error('Set POLY_PIZZA_KEY (never commit it).');
    const res = await fetch(`${API}/${path}`, { headers: { 'x-auth-token': key } });
    if (!res.ok) throw new Error(`${path}: HTTP ${res.status}`);
    return res.json();
};

async function search() {
    const terms = JSON.parse(readFileSync(join(HERE, 'terms.json'), 'utf8'));
    const picked = new Set(JSON.parse(readFileSync(join(HERE, 'picks.json'), 'utf8')).map((p) => p.id));
    const seen = new Map();
    for (const [archetype, words] of Object.entries(terms))
        for (const term of words) {
            const { results } = await api(`search/${encodeURIComponent(term)}?Limit=32&License=1`);
            for (const r of results) {
                const creator = CREATORS[r.Creator.Username];
                if (!creator || r.Animated || r['Tri Count'] > MAX_TRIS || seen.has(r.ID)) continue;
                seen.set(r.ID, {
                    id: r.ID, title: r.Title, creator, tris: r['Tri Count'], category: r.Category, tags: r.Tags,
                    thumbnail: r.Thumbnail, archetype, term, picked: picked.has(r.ID),
                });
            }
        }
    const list = [...seen.values()];
    writeFileSync(join(HERE, 'shortlist.json'), `${JSON.stringify(list, null, 1)}\n`);
    writeFileSync(join(HERE, 'shortlist.html'), sheet(list));
    console.log(`${list.length} candidates → tools/catalogue/shortlist.html; add the keepers to picks.json`);
}

const esc = (s) => String(s).replace(/[&<>"]/g, (c) => `&#${c.charCodeAt(0)};`);

function sheet(list) {
    const groups = Object.groupBy(list, (m) => m.archetype);
    const cards = (ms) => ms.map((m) => `<figure class="${m.picked ? 'picked' : ''}"><img loading="lazy" src="${esc(m.thumbnail)}"><figcaption><b>${esc(m.title)}</b><code>${esc(m.id)}</code><span>${esc(m.creator)} · ${m.tris} tris · “${esc(m.term)}”</span></figcaption></figure>`).join('');
    return `<!doctype html><meta charset=utf-8><title>Catalogue shortlist</title><style>
body{margin:0;font:13px Tahoma,sans-serif;background:#ece9d8}main{padding:12px;max-width:1200px;margin:auto}
h2{margin:12px 0 4px;font-size:13px;color:#fff;background:#3a6ea5;padding:4px 8px}
div{display:grid;grid-template-columns:repeat(auto-fill,minmax(140px,1fr));gap:6px}
figure{margin:0;background:#fff;border:1px solid #bbb}figure.picked{outline:3px solid #3a9b3a}
img{width:100%;aspect-ratio:1;object-fit:contain;background:#dfe6ee}figcaption{display:flex;flex-direction:column;padding:3px 5px;font-size:11px}
code{user-select:all;color:#36c}</style><main data-drostje-artifact><p>Green = already in picks.json. Copy an id into picks.json to keep it.</p>
${Object.entries(groups).map(([a, ms]) => `<h2>${esc(a)}</h2><div>${cards(ms)}</div>`).join('')}</main>`;
}

async function fetchPicks() {
    const picks = JSON.parse(readFileSync(join(HERE, 'picks.json'), 'utf8'));
    const entries = [];
    const licences = {};
    for (const pick of picks) {
        const m = await api(`model/${pick.id}`);
        const creator = CREATORS[m.Creator.Username];
        if (!creator || !String(m.Licence).startsWith('CC0')) throw new Error(`${pick.id}: only whitelisted CC0 models`);
        const base = pick.name.replace(/^[a-z]+-/, '');
        // The prefix says whose model it is (k- Kenney, kl- Kay Lousberg, q- Quaternius), whatever picks.json guessed.
        pick.name = `${PREFIX[creator]}${base}`;
        const file = `${pick.file ?? base}.glb`;
        const res = await fetch(m.Download, { headers: { 'User-Agent': 'Mozilla/5.0 proompting-catalogue' } });
        if (!res.ok) throw new Error(`${pick.id}: download HTTP ${res.status}`);
        const glb = Buffer.from(await res.arrayBuffer());
        // Colour must live in the materials: the client draws flat vertex colours and drops textures.
        if (textured(glb)) {
            console.log(`✗ ${pick.name} (${m.Title}) is textured; pick a flat-coloured model instead`);
            continue;
        }
        mkdirSync(join(MODELS, creator), { recursive: true });
        writeFileSync(join(MODELS, creator, file), glb);
        (licences[creator] ??= []).push(`${file.padEnd(28)} https://poly.pizza/m/${pick.id}  "${m.Title}"`);
        entries.push(prefabFor(pick, creator, file, measure(glb), m));
        console.log(`✓ ${pick.name} (${m.Title}, ${glb.length >> 10} kB)`);
    }
    for (const [creator, lines] of Object.entries(licences))
        writeFileSync(join(MODELS, creator, 'LICENSE.txt'),
            `Models by ${Object.keys(CREATORS).find((k) => CREATORS[k] === creator)}, CC0 1.0 (public domain), via Poly Pizza.\n${lines.join('\n')}\n`);
    writeFileSync(join(HERE, 'picks.json'), `${JSON.stringify(picks, null, 4)}\n`);
    writeGenerated(entries);
    writeBackend(entries);
}

/** Draft entry: size and fit from the measured box unless picks.json says otherwise. */
function prefabFor(pick, creator, file, box, meta) {
    const building = /building/i.test(meta.Category ?? '');
    const unit = UNIT[creator];
    const [w, h, d] = box.map((v) => v * unit);
    const size = pick.size ?? (building ? [3, 3] : [Math.max(1, Math.ceil(w - 0.05)), Math.max(1, Math.ceil(d - 0.05))]);
    const fit = pick.fit ?? [size[0] * 0.95, building ? 0 : round(h), size[1] * 0.95];
    const prefab = {
        ...(size[0] !== 1 || size[1] !== 1 ? { size } : {}),
        ...(pick.solid === false ? { solid: false } : {}),
        ...(pick.tint ? { tint: pick.tint } : {}),
        ...(pick.seats ? { seats: pick.seats } : {}),
        parts: [{ glb: `/models/${creator}/${file}`, fit, c: pick.c ?? '#8c8c8c', ...(pick.mats ? { mats: pick.mats } : {}) }],
    };
    return { name: pick.name, prefab, about: `${pick.about ?? meta.Title.toLowerCase()} (${meta.Creator.Username} model)` };
}

const round = (v) => Math.round(v * 100) / 100;

function writeGenerated(entries) {
    const body = entries.map((e) => `    '${e.name}': ${JSON.stringify(e.prefab)},`).join('\n');
    writeFileSync(GENERATED, `// Generated by tools/catalogue/harvest.mjs from picks.json; edit picks.json, then run fetch.
import type { Prefab } from './prefab';

/** Catalogue models (ADR 0027): curated CC0 GLBs, self-hosted in public/models. */
export const CATALOGUE: Record<string, Prefab> = {
${body}
};
`);
}

/** prefabs.json keeps primitives first, then the catalogue in picks order (the frontend test checks both lists match). */
function writeBackend(entries) {
    const raw = readFileSync(BACKEND, 'utf8');
    const prefixes = Object.values(PREFIX);
    const kept = JSON.parse(raw).filter((p) => !prefixes.some((x) => p.name.startsWith(x)));
    const added = entries.map((e) => ({
        name: e.name, size: e.prefab.size ?? [1, 1], solid: e.prefab.solid !== false, wall: false,
        seats: (e.prefab.seats ?? []).length, about: e.about,
    }));
    writeFileSync(BACKEND, `${JSON.stringify([...kept, ...added], null, 1)}\n`);
}

const gltfOf = (glb) => JSON.parse(glb.subarray(20, 20 + glb.readUInt32LE(12)).toString('utf8'));

const textured = (glb) => (gltfOf(glb).materials ?? []).some((m) => m.pbrMetallicRoughness?.baseColorTexture);

// ---- GLB measuring: world-space box of every mesh, node transforms applied ----

function measure(glb) {
    const gltf = gltfOf(glb);
    const min = [Infinity, Infinity, Infinity];
    const max = [-Infinity, -Infinity, -Infinity];
    const visit = (index, parent) => {
        const node = gltf.nodes[index];
        const world = mul(parent, local(node));
        if (node.mesh !== undefined)
            for (const prim of gltf.meshes[node.mesh].primitives) {
                const a = gltf.accessors[prim.attributes.POSITION];
                for (const corner of corners(a.min, a.max)) {
                    const p = apply(world, corner);
                    for (let i = 0; i < 3; i++) {
                        min[i] = Math.min(min[i], p[i]);
                        max[i] = Math.max(max[i], p[i]);
                    }
                }
            }
        for (const child of node.children ?? []) visit(child, world);
    };
    for (const root of gltf.scenes[gltf.scene ?? 0].nodes) visit(root, IDENTITY);
    return max.map((v, i) => v - min[i]);
}

const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

function local(node) {
    if (node.matrix) return node.matrix;
    const [x, y, z, w] = node.rotation ?? [0, 0, 0, 1];
    const [sx, sy, sz] = node.scale ?? [1, 1, 1];
    const [tx, ty, tz] = node.translation ?? [0, 0, 0];
    return [
        (1 - 2 * (y * y + z * z)) * sx, 2 * (x * y + z * w) * sx, 2 * (x * z - y * w) * sx, 0,
        2 * (x * y - z * w) * sy, (1 - 2 * (x * x + z * z)) * sy, 2 * (y * z + x * w) * sy, 0,
        2 * (x * z + y * w) * sz, 2 * (y * z - x * w) * sz, (1 - 2 * (x * x + y * y)) * sz, 0,
        tx, ty, tz, 1,
    ];
}

/** Column-major 4×4 product a·b. */
function mul(a, b) {
    const out = new Array(16).fill(0);
    for (let c = 0; c < 4; c++)
        for (let r = 0; r < 4; r++)
            for (let k = 0; k < 4; k++) out[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k];
    return out;
}

const apply = (m, [x, y, z]) => [0, 1, 2].map((r) => m[r] * x + m[4 + r] * y + m[8 + r] * z + m[12 + r]);

const corners = (lo, hi) =>
    [0, 1, 2, 3, 4, 5, 6, 7].map((i) => [i & 1 ? hi[0] : lo[0], i & 2 ? hi[1] : lo[1], i & 4 ? hi[2] : lo[2]]);

const step = process.argv[2];
if (step === 'search') await search();
else if (step === 'fetch') await fetchPicks();
else {
    console.log('usage: POLY_PIZZA_KEY=… node tools/catalogue/harvest.mjs search|fetch');
    process.exitCode = 1;
}
