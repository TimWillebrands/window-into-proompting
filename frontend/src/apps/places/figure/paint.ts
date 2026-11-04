import type {
    BottomDoc,
    Hex,
    LegwearDoc,
    PatternDoc,
    PrintDoc,
    TopDoc,
} from './doc';
import { PANELS, type Panel, type PanelFrame } from './unwrap';

type Ctx = CanvasRenderingContext2D;

const px = (ctx: Ctx, p: Panel) => {
    const { width: W, height: Hh } = ctx.canvas;
    const [u, v, w, h] = PANELS[p];
    return { x: u * W, y: (1 - v - h) * Hh, w: w * W, h: h * Hh };
};

const shade = (hex: Hex, k: number) => {
    const n = Number.parseInt(hex.slice(1), 16);
    const f = (c: number) =>
        Math.round(k < 0 ? c * (1 + k) : c + (255 - c) * k);
    const rgb = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map(f);
    return `rgb(${rgb.join(',')})`;
};

/** Runs f with drawing clipped to a panel and the origin at its top left. */
function inPanel(ctx: Ctx, p: Panel, f: (w: number, h: number) => void) {
    const r = px(ctx, p);
    ctx.save();
    ctx.beginPath();
    ctx.rect(r.x, r.y, r.w, r.h);
    ctx.clip();
    ctx.translate(r.x, r.y);
    f(r.w, r.h);
    ctx.restore();
}

function tile(ctx: Ctx, w: number, h: number, pat: PatternDoc, size: number) {
    const [a, b = shade(a, -0.3), c = b] = pat.colors;
    switch (pat.kind) {
        case 'stripes':
            for (let y = 0, i = 0; y < h; y += size, i++) {
                ctx.fillStyle = i % 2 ? b : a;
                ctx.fillRect(0, y, w, size);
            }
            break;
        case 'checker':
            for (let y = 0, i = 0; y < h; y += size, i++)
                for (let x = 0, j = 0; x < w; x += size, j++) {
                    ctx.fillStyle = (i + j) % 2 ? b : a;
                    ctx.fillRect(x, y, size, size);
                }
            break;
        case 'plaid': {
            ctx.fillStyle = a;
            ctx.fillRect(0, 0, w, h);
            ctx.globalAlpha = 0.55;
            ctx.fillStyle = b;
            for (let y = 0; y < h; y += size * 2)
                ctx.fillRect(0, y, w, size * 0.9);
            for (let x = 0; x < w; x += size * 2)
                ctx.fillRect(x, 0, size * 0.9, h);
            ctx.globalAlpha = 0.8;
            ctx.fillStyle = c;
            for (let y = size * 0.45; y < h; y += size * 2)
                ctx.fillRect(0, y, w, size * 0.08);
            for (let x = size * 0.45; x < w; x += size * 2)
                ctx.fillRect(x, 0, size * 0.08, h);
            ctx.globalAlpha = 1;
            break;
        }
        case 'dots':
            ctx.fillStyle = a;
            ctx.fillRect(0, 0, w, h);
            ctx.fillStyle = b;
            for (let y = 0, i = 0; y < h + size; y += size, i++)
                for (let x = (i % 2) * size * 0.5; x < w + size; x += size) {
                    ctx.beginPath();
                    ctx.arc(x, y, size * 0.2, 0, Math.PI * 2);
                    ctx.fill();
                }
            break;
        case 'stars':
            ctx.fillStyle = a;
            ctx.fillRect(0, 0, w, h);
            ctx.fillStyle = b;
            for (let y = 0, i = 0; y < h + size; y += size, i++)
                for (let x = (i % 2) * size * 0.5; x < w + size; x += size) {
                    ctx.beginPath();
                    for (let k = 0; k < 10; k++) {
                        const r = size * (k % 2 ? 0.1 : 0.26);
                        const an = (k / 10) * Math.PI * 2 - Math.PI / 2;
                        ctx.lineTo(x + Math.cos(an) * r, y + Math.sin(an) * r);
                    }
                    ctx.closePath();
                    ctx.fill();
                }
            break;
        case 'fishnet':
            ctx.clearRect(0, 0, w, h);
            ctx.strokeStyle = a;
            ctx.lineWidth = Math.max(1.5, size * 0.13);
            for (let k = -h; k < w + h; k += size) {
                ctx.beginPath();
                ctx.moveTo(k, 0);
                ctx.lineTo(k + h, h);
                ctx.moveTo(k + h, 0);
                ctx.lineTo(k, h);
                ctx.stroke();
            }
            break;
        case 'fade': {
            const g = ctx.createLinearGradient(0, 0, 0, h);
            pat.colors.forEach((col, i) => {
                g.addColorStop(i / Math.max(1, pat.colors.length - 1), col);
            });
            ctx.fillStyle = g;
            ctx.fillRect(0, 0, w, h);
            break;
        }
    }
}

function pattern(ctx: Ctx, pat: PatternDoc) {
    const W = ctx.canvas.width;
    const size = Math.max(6, W * (0.018 + pat.scale * 0.07));
    for (const p of ['front', 'back'] as const)
        inPanel(ctx, p, (w, h) => tile(ctx, w, h, pat, size));
    for (const p of ['armL', 'armR', 'legL', 'legR'] as const)
        inPanel(ctx, p, (w, h) => {
            // Bands run round a limb: turn the panel so its length is the pattern's height.
            ctx.save();
            ctx.translate(w, 0);
            ctx.rotate(Math.PI / 2);
            tile(ctx, h, w, pat, size);
            ctx.restore();
        });
}

export type Images = Map<string, HTMLImageElement>;

/** Rasterises inline SVG through an <img>, which can't run scripts or fetch anything. */
export function loadPrints(svgs: string[]): Promise<Images> {
    const out: Images = new Map();
    return Promise.all(
        svgs.map(
            (svg) =>
                new Promise<void>((done) => {
                    const img = new Image();
                    img.onload = () => {
                        out.set(svg, img);
                        done();
                    };
                    img.onerror = () => done();
                    img.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
                }),
        ),
    ).then(() => out);
}

function prints(
    ctx: Ctx,
    list: PrintDoc[],
    images: Images,
    frame: PanelFrame,
    chestY: number,
    bellyY: number,
) {
    for (const p of list) {
        const img = images.get(p.svg);
        if (!img) continue;
        const panel: Panel =
            p.at === 'back'
                ? 'back'
                : p.at === 'sleeveL'
                  ? 'armL'
                  : p.at === 'sleeveR'
                    ? 'armR'
                    : p.at === 'thighL'
                      ? 'legL'
                      : p.at === 'thighR'
                        ? 'legR'
                        : 'front';
        inPanel(ctx, panel, (w, h) => {
            if (panel === 'front' || panel === 'back') {
                const y = p.at === 'belly' ? bellyY : chestY;
                const cy = (1 - (y - frame.y0) / (frame.y1 - frame.y0)) * h;
                const size = w * (0.18 + p.size * 0.42);
                ctx.drawImage(img, w / 2 - size / 2, cy - size / 2, size, size);
            } else {
                // Along the limb, sitting on its outside.
                const size = h * (0.35 + p.size * 0.4);
                ctx.save();
                ctx.translate(w * 0.32, h * 0.5);
                ctx.rotate(Math.PI / 2);
                ctx.drawImage(img, -size / 2, -size / 2, size, size);
                ctx.restore();
            }
        });
    }
}

const rib = (
    ctx: Ctx,
    x: number,
    y: number,
    w: number,
    h: number,
    c: string,
) => {
    ctx.fillStyle = c;
    ctx.fillRect(x, y, w, h);
    ctx.fillStyle = 'rgba(0,0,0,.16)';
    const step = Math.max(2, ctx.canvas.width / 180);
    for (let i = x; i < x + w; i += step * 2) ctx.fillRect(i, y, step * 0.6, h);
};

const line = (
    ctx: Ctx,
    pts: number[][],
    c: string,
    width: number,
    dash?: number[],
) => {
    ctx.strokeStyle = c;
    ctx.lineWidth = width;
    ctx.setLineDash(dash ?? []);
    ctx.beginPath();
    pts.forEach(([x, y], i) => {
        if (i) ctx.lineTo(x, y);
        else ctx.moveTo(x, y);
    });
    ctx.stroke();
    ctx.setLineDash([]);
};

export interface ClothInfo {
    frame: PanelFrame;
    chestY: number;
    bellyY: number;
    H: number;
}

/** A top's atlas: colour, pattern, prints, then construction: cuffs, hems, pockets, zips. */
export function paintTop(
    ctx: Ctx,
    doc: TopDoc,
    info: ClothInfo,
    images: Images,
) {
    const { width: W, height: Hh } = ctx.canvas;
    ctx.fillStyle = doc.color;
    ctx.fillRect(0, 0, W, Hh);
    if (doc.pattern) pattern(ctx, doc.pattern);
    if (doc.sleeveColor)
        for (const p of ['armL', 'armR'] as const)
            inPanel(ctx, p, (w, h) => {
                ctx.fillStyle = doc.sleeveColor as Hex;
                ctx.fillRect(0, 0, w, h);
            });
    prints(ctx, doc.prints ?? [], images, info.frame, info.chestY, info.bellyY);
    const trim = doc.trim ?? shade(doc.color, -0.28);
    const seam = shade(doc.color, -0.32);
    const lw = Math.max(1, W / 340);
    const yOf = (y: number, h: number) =>
        (1 - (y - info.frame.y0) / (info.frame.y1 - info.frame.y0)) * h;
    const heavy =
        doc.hood ||
        doc.neck === 'collar' ||
        (doc.sleeve > 0.9 && doc.fit > 0.5);
    for (const p of ['front', 'back'] as const)
        inPanel(ctx, p, (w, h) => {
            const band = h * (heavy ? 0.05 : 0.018);
            if (heavy) rib(ctx, 0, h - band, w, band, trim);
            else
                line(
                    ctx,
                    [
                        [0, h - band],
                        [w, h - band],
                    ],
                    seam,
                    lw,
                    [lw * 3, lw * 2],
                );
            if (p === 'front') {
                const mid = w / 2;
                if (doc.hood && !doc.open) {
                    // Kangaroo pocket.
                    const top = yOf(info.bellyY + 0.035 * info.H, h);
                    const bot = h - band;
                    const half = w * 0.2;
                    ctx.fillStyle = shade(doc.color, -0.1);
                    ctx.beginPath();
                    ctx.moveTo(mid - half * 0.72, top);
                    ctx.lineTo(mid + half * 0.72, top);
                    ctx.lineTo(mid + half * 1.05, top + (bot - top) * 0.55);
                    ctx.lineTo(mid + half * 1.05, bot);
                    ctx.lineTo(mid - half * 1.05, bot);
                    ctx.lineTo(mid - half * 1.05, top + (bot - top) * 0.55);
                    ctx.closePath();
                    ctx.fill();
                    ctx.strokeStyle = seam;
                    ctx.lineWidth = lw;
                    ctx.stroke();
                }
                if (doc.hood) {
                    const y0 = yOf(info.chestY + 0.075 * info.H, h);
                    const y1 = yOf(info.chestY - 0.005 * info.H, h);
                    for (const s of [-1, 1])
                        line(
                            ctx,
                            [
                                [mid + s * w * 0.03, y0],
                                [mid + s * w * 0.036, y1],
                            ],
                            '#efeae2',
                            lw * 2,
                        );
                }
                if (doc.open)
                    line(
                        ctx,
                        [
                            [mid - w * 0.03, 0],
                            [mid - w * 0.03, h],
                        ],
                        trim,
                        lw * 2,
                    );
                if (doc.neck === 'collar' && !doc.open) {
                    line(
                        ctx,
                        [
                            [mid, yOf(info.chestY + 0.08 * info.H, h)],
                            [mid, h],
                        ],
                        seam,
                        lw,
                    );
                    for (let i = 0; i < 5; i++) {
                        ctx.fillStyle = shade(doc.color, 0.45);
                        ctx.beginPath();
                        ctx.arc(
                            mid + lw * 3,
                            yOf(info.chestY + 0.05 * info.H, h) + i * h * 0.075,
                            lw * 1.6,
                            0,
                            7,
                        );
                        ctx.fill();
                    }
                }
            }
        });
    for (const p of ['armL', 'armR'] as const)
        inPanel(ctx, p, (w, h) => {
            const end = doc.sleeve * w;
            const cuff = w * (heavy ? 0.06 : 0.02);
            if (doc.trim || heavy)
                rib(ctx, end - cuff * 1.3, 0, cuff * 1.3, h, trim);
            else
                line(
                    ctx,
                    [
                        [end - cuff, 0],
                        [end - cuff, h],
                    ],
                    seam,
                    lw,
                    [lw * 3, lw * 2],
                );
        });
}

export function paintBottom(
    ctx: Ctx,
    doc: BottomDoc,
    info: ClothInfo,
    images: Images,
) {
    const { width: W, height: Hh } = ctx.canvas;
    ctx.fillStyle = doc.color;
    ctx.fillRect(0, 0, W, Hh);
    if (doc.pattern) pattern(ctx, doc.pattern);
    prints(ctx, doc.prints ?? [], images, info.frame, info.chestY, info.bellyY);
    const seam = shade(doc.color, -0.34);
    const stitch = doc.trim ?? shade(doc.color, 0.32);
    const lw = Math.max(1, W / 340);
    for (const p of ['front', 'back'] as const)
        inPanel(ctx, p, (w, h) => {
            const band = h * 0.05;
            ctx.fillStyle = shade(doc.color, -0.16);
            ctx.fillRect(0, 0, w, band);
            line(
                ctx,
                [
                    [0, band],
                    [w, band],
                ],
                stitch,
                lw,
                [lw * 3, lw * 2],
            );
            if (doc.kind === 'pants') {
                const mid = w / 2;
                if (p === 'front') {
                    line(
                        ctx,
                        [
                            [mid, band],
                            [mid, h * 0.32],
                        ],
                        seam,
                        lw * 1.5,
                    );
                    line(
                        ctx,
                        [
                            [mid + w * 0.05, band],
                            [mid + w * 0.05, h * 0.22],
                            [mid, h * 0.3],
                        ],
                        stitch,
                        lw,
                        [lw * 3, lw * 2],
                    );
                    for (const s of [-1, 1])
                        line(
                            ctx,
                            [
                                [mid + s * w * 0.3, band],
                                [mid + s * w * 0.17, h * 0.14],
                                [mid + s * w * 0.13, band],
                            ],
                            stitch,
                            lw,
                            [lw * 3, lw * 2],
                        );
                } else {
                    for (const s of [-1, 1]) {
                        const cx = mid + s * w * 0.13;
                        ctx.fillStyle = shade(doc.color, -0.08);
                        ctx.fillRect(
                            cx - w * 0.07,
                            band * 2,
                            w * 0.14,
                            h * 0.15,
                        );
                        ctx.strokeStyle = stitch;
                        ctx.lineWidth = lw;
                        ctx.setLineDash([lw * 3, lw * 2]);
                        ctx.strokeRect(
                            cx - w * 0.07,
                            band * 2,
                            w * 0.14,
                            h * 0.15,
                        );
                        ctx.setLineDash([]);
                    }
                }
            }
        });
    for (const p of ['legL', 'legR'] as const)
        inPanel(ctx, p, (w, h) => {
            // angle 0 (panel middle) is the front of the leg.
            line(
                ctx,
                [
                    [0, h * 0.25],
                    [w, h * 0.25],
                ],
                seam,
                lw,
                [lw * 3, lw * 2],
            );
            line(
                ctx,
                [
                    [0, h * 0.75],
                    [w, h * 0.75],
                ],
                seam,
                lw,
                [lw * 3, lw * 2],
            );
            if (doc.rips) {
                ctx.fillStyle = '#efe9dc';
                const kx = w * 0.46;
                ctx.fillRect(
                    kx - w * 0.03 * doc.rips,
                    h * 0.38,
                    w * 0.06 * doc.rips,
                    h * 0.24,
                );
                for (let i = 0; i < 4; i++)
                    line(
                        ctx,
                        [
                            [kx - w * 0.04, h * (0.4 + i * 0.06)],
                            [kx + w * 0.04, h * (0.41 + i * 0.06)],
                        ],
                        '#ffffffaa',
                        lw,
                    );
            }
            const end = w * (0.12 + doc.length * 0.9);
            if (doc.trim) rib(ctx, end - w * 0.04, 0, w * 0.04, h, doc.trim);
        });
}

export function paintLegwear(ctx: Ctx, doc: LegwearDoc) {
    const { width: W, height: Hh } = ctx.canvas;
    ctx.clearRect(0, 0, W, Hh);
    ctx.fillStyle = doc.color;
    if (doc.pattern?.kind !== 'fishnet') ctx.fillRect(0, 0, W, Hh);
    if (doc.pattern) pattern(ctx, doc.pattern);
}
