import type { FaceDoc } from './doc';

/** Head space (±0.5 head heights, y from the head's centre) → canvas pixels. */
const px = (size: number) => ({
    x: (u: number) => (u + 0.5) * size,
    y: (v: number) => (1 - (v + 0.55)) * size,
    s: (u: number) => u * size,
});

export interface FaceFrame {
    /** 1 open, 0 shut. */
    open: number;
    /** 0 shut, 1 wide. */
    talk: number;
    /** -1..1 on top of the resting set. */
    smile: number;
}

function eye(
    ctx: CanvasRenderingContext2D,
    doc: FaceDoc,
    size: number,
    side: 1 | -1,
    frame: FaceFrame,
) {
    const { x, y, s } = px(size);
    const e = doc.eyes;
    const cx = x(0.175 * side);
    const cy = y(-0.05);
    const w = s(0.11 + e.size * 0.045);
    const tall = s(0.085 + e.size * 0.05);
    const h =
        tall *
        (e.shape === 'round' || e.shape === 'wide'
            ? 1.2
            : e.shape === 'droopy'
              ? 0.92
              : 1.02) *
        frame.open;
    const tilt =
        (e.shape === 'cat'
            ? 0.3
            : e.shape === 'almond'
              ? 0.15
              : e.shape === 'droopy'
                ? -0.16
                : 0.04) * side;
    ctx.save();
    ctx.translate(cx, cy);
    ctx.scale(side, 1);
    ctx.rotate(-Math.abs(tilt) * Math.sign(tilt) * side);
    const outline = () => {
        ctx.beginPath();
        ctx.moveTo(-w * 0.95, h * 0.1);
        ctx.bezierCurveTo(
            -w * 0.55,
            -h * 1.15,
            w * 0.45,
            -h * 1.15,
            w,
            -h * 0.08,
        );
        ctx.bezierCurveTo(
            w * 0.6,
            h * 0.82,
            -w * 0.5,
            h * 0.92,
            -w * 0.95,
            h * 0.1,
        );
        ctx.closePath();
    };
    if (e.shadow) {
        ctx.save();
        ctx.globalAlpha = 0.55;
        ctx.fillStyle = e.shadow;
        ctx.beginPath();
        ctx.ellipse(
            w * 0.1,
            -h * 0.5,
            w * 1.35,
            tall * 1.25,
            0,
            0,
            Math.PI * 2,
        );
        ctx.fill();
        ctx.restore();
    }
    if (frame.open > 0.15) {
        ctx.fillStyle = '#fdfaf6';
        outline();
        ctx.fill();
        ctx.save();
        outline();
        ctx.clip();
        const r = tall * 0.74;
        ctx.fillStyle = e.iris;
        ctx.beginPath();
        ctx.arc(-w * 0.03, h * 0.02, r, 0, Math.PI * 2);
        ctx.fill();
        ctx.lineWidth = r * 0.16;
        ctx.strokeStyle = 'rgba(0,0,0,.45)';
        ctx.stroke();
        ctx.fillStyle = '#0c0b10';
        ctx.beginPath();
        ctx.arc(-w * 0.03, h * 0.02, r * 0.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = 'rgba(255,255,255,.95)';
        ctx.beginPath();
        ctx.arc(
            -w * 0.03 - r * 0.32 * side,
            h * 0.02 - r * 0.38,
            r * 0.3,
            0,
            Math.PI * 2,
        );
        ctx.fill();
        ctx.fillStyle = 'rgba(0,0,0,.12)';
        ctx.fillRect(-w * 1.1, -h * 1.3, w * 2.2, h * 0.55);
        ctx.restore();
    }
    // Upper lid carries the look: liner, wing, lashes.
    const liner = s(0.008 + e.liner * 0.03);
    ctx.strokeStyle = e.linerColor;
    ctx.fillStyle = e.linerColor;
    ctx.lineCap = 'round';
    ctx.lineWidth = liner;
    ctx.beginPath();
    if (frame.open > 0.15) {
        ctx.moveTo(-w * 0.95, h * 0.1);
        ctx.bezierCurveTo(
            -w * 0.55,
            -h * 1.15,
            w * 0.45,
            -h * 1.15,
            w,
            -h * 0.08,
        );
    } else {
        ctx.moveTo(-w * 0.95, 0);
        ctx.quadraticCurveTo(0, h * 0.5 + s(0.03), w, 0);
    }
    ctx.stroke();
    if (e.wing > 0) {
        ctx.beginPath();
        ctx.moveTo(w * 0.72, -h * 0.52);
        ctx.lineTo(w * (1.2 + e.wing * 0.55), -h * 0.2 - tall * e.wing * 0.7);
        ctx.lineTo(w * 1.0, h * 0.06);
        ctx.closePath();
        ctx.fill();
    }
    if (e.liner > 0.4 && frame.open > 0.15) {
        ctx.lineWidth = liner * 0.65;
        ctx.beginPath();
        ctx.moveTo(w, -h * 0.08);
        ctx.bezierCurveTo(
            w * 0.6,
            h * 0.82,
            -w * 0.5,
            h * 0.92,
            -w * 0.95,
            h * 0.1,
        );
        ctx.stroke();
    }
    ctx.restore();
}

function brow(
    ctx: CanvasRenderingContext2D,
    doc: FaceDoc,
    skin: string,
    size: number,
    side: 1 | -1,
) {
    const { x, y, s } = px(size);
    const b = doc.brows;
    const inner = [x(0.085 * side), y(0.115)];
    const peak = [x(0.2 * side), y(0.145 + b.arch * 0.05)];
    const outer = [x(0.31 * side), y(0.085 + b.arch * 0.02)];
    const w = s(0.012 + b.weight * 0.036);
    ctx.fillStyle = b.color;
    ctx.beginPath();
    ctx.moveTo(inner[0], inner[1] - w * 0.55);
    ctx.quadraticCurveTo(peak[0], peak[1] - w * 0.7, outer[0], outer[1]);
    ctx.quadraticCurveTo(
        peak[0],
        peak[1] + w * 0.5,
        inner[0],
        inner[1] + w * 0.55,
    );
    ctx.closePath();
    ctx.fill();
    if (b.slit && side === 1) {
        ctx.save();
        ctx.translate(x(0.25), y(0.13));
        ctx.rotate(0.25);
        ctx.fillStyle = skin;
        ctx.fillRect(-s(0.009), -s(0.06), s(0.018), s(0.12));
        ctx.restore();
    }
}

function mouth(
    ctx: CanvasRenderingContext2D,
    doc: FaceDoc,
    size: number,
    frame: FaceFrame,
) {
    const { x, y, s } = px(size);
    const m = doc.mouth;
    const w = s(0.08 + m.width * 0.055);
    const cy = y(-0.29);
    const cx = x(0);
    const curve = (m.set + frame.smile) * s(0.022);
    const lip = s(0.012 + m.lips * 0.024);
    const open = frame.talk * s(0.055);
    if (open > 0.3) {
        ctx.fillStyle = '#3a1218';
        ctx.beginPath();
        ctx.ellipse(
            cx,
            cy + open * 0.35,
            w * 0.72,
            open * 0.85,
            0,
            0,
            Math.PI * 2,
        );
        ctx.fill();
        ctx.fillStyle = '#f5ede6';
        ctx.fillRect(cx - w * 0.46, cy - open * 0.5, w * 0.92, open * 0.42);
    }
    ctx.fillStyle = m.color;
    ctx.beginPath();
    ctx.moveTo(cx - w, cy - curve);
    ctx.bezierCurveTo(
        cx - w * 0.5,
        cy - lip * 1.1,
        cx - w * 0.16,
        cy - lip * 1.25,
        cx,
        cy - lip * 0.72,
    );
    ctx.bezierCurveTo(
        cx + w * 0.16,
        cy - lip * 1.25,
        cx + w * 0.5,
        cy - lip * 1.1,
        cx + w,
        cy - curve,
    );
    ctx.bezierCurveTo(
        cx + w * 0.5,
        cy + curve * 0.4,
        cx - w * 0.5,
        cy + curve * 0.4,
        cx - w,
        cy - curve,
    );
    ctx.closePath();
    ctx.fill();
    ctx.beginPath();
    ctx.moveTo(cx - w, cy - curve);
    ctx.bezierCurveTo(
        cx - w * 0.5,
        cy + curve * 0.4 + open * 0.2,
        cx + w * 0.5,
        cy + curve * 0.4 + open * 0.2,
        cx + w,
        cy - curve,
    );
    ctx.bezierCurveTo(
        cx + w * 0.45,
        cy + lip * 1.5 + open,
        cx - w * 0.45,
        cy + lip * 1.5 + open,
        cx - w,
        cy - curve,
    );
    ctx.closePath();
    ctx.fill();
    ctx.strokeStyle = 'rgba(30,8,10,.65)';
    ctx.lineWidth = s(0.008);
    ctx.lineCap = 'round';
    ctx.beginPath();
    ctx.moveTo(cx - w * 1.02, cy - curve);
    ctx.bezierCurveTo(
        cx - w * 0.5,
        cy + curve * 0.4 + open * 0.2,
        cx + w * 0.5,
        cy + curve * 0.4 + open * 0.2,
        cx + w * 1.02,
        cy - curve,
    );
    ctx.stroke();
}

const METAL = '#dfe3ea';

function piercings(ctx: CanvasRenderingContext2D, doc: FaceDoc, size: number) {
    const { x, y, s } = px(size);
    const ball = (u: number, v: number, r = 0.011) => {
        ctx.fillStyle = METAL;
        ctx.beginPath();
        ctx.arc(x(u), y(v), s(r), 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = 'rgba(40,40,60,.55)';
        ctx.beginPath();
        ctx.arc(
            x(u) + s(r * 0.3),
            y(v) + s(r * 0.3),
            s(r * 0.45),
            0,
            Math.PI * 2,
        );
        ctx.fill();
    };
    const hoop = (u: number, v: number, r: number) => {
        ctx.strokeStyle = METAL;
        ctx.lineWidth = s(0.008);
        ctx.beginPath();
        ctx.arc(x(u), y(v), s(r), 0, Math.PI * 2);
        ctx.stroke();
    };
    for (const p of doc.piercings ?? [])
        switch (p) {
            case 'brow':
                ball(0.28, 0.13);
                ball(0.31, 0.075);
                break;
            case 'nose':
                ball(0.035, -0.185, 0.009);
                break;
            case 'septum':
                hoop(0, -0.215, 0.018);
                break;
            case 'lip':
                hoop(0.05, -0.325, 0.016);
                break;
            case 'snakebites':
                ball(0.07, -0.335);
                ball(-0.07, -0.335);
                break;
            case 'labret':
                ball(0, -0.36);
                break;
        }
}

function facialHair(ctx: CanvasRenderingContext2D, doc: FaceDoc, size: number) {
    const { x, y, s } = px(size);
    const kind = doc.facialHair;
    if (!kind) return;
    const c = doc.facialHairColor ?? doc.brows.color;
    ctx.fillStyle = c;
    if (kind === 'stubble') {
        ctx.globalAlpha = 0.28;
        ctx.beginPath();
        ctx.ellipse(x(0), y(-0.33), s(0.27), s(0.14), 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.globalAlpha = 1;
        return;
    }
    const tache = () => {
        ctx.beginPath();
        ctx.moveTo(x(-0.13), y(-0.27));
        ctx.quadraticCurveTo(x(-0.05), y(-0.21), x(0), y(-0.235));
        ctx.quadraticCurveTo(x(0.05), y(-0.21), x(0.13), y(-0.27));
        ctx.quadraticCurveTo(x(0), y(-0.26), x(-0.13), y(-0.27));
        ctx.fill();
    };
    if (kind === 'moustache') tache();
    if (kind === 'soul-patch') {
        ctx.beginPath();
        ctx.ellipse(x(0), y(-0.355), s(0.025), s(0.03), 0, 0, Math.PI * 2);
        ctx.fill();
    }
    if (kind === 'goatee' || kind === 'beard') {
        tache();
        ctx.beginPath();
        const w = kind === 'beard' ? 0.34 : 0.1;
        ctx.moveTo(x(-w), y(kind === 'beard' ? -0.08 : -0.33));
        ctx.quadraticCurveTo(x(-w * 0.9), y(-0.46), x(0), y(-0.5));
        ctx.quadraticCurveTo(
            x(w * 0.9),
            y(-0.46),
            x(w),
            y(kind === 'beard' ? -0.08 : -0.33),
        );
        ctx.quadraticCurveTo(x(w * 0.55), y(-0.34), x(0.09), y(-0.325));
        ctx.lineTo(x(-0.09), y(-0.325));
        ctx.quadraticCurveTo(
            x(-w * 0.55),
            y(-0.34),
            x(-w),
            y(kind === 'beard' ? -0.08 : -0.33),
        );
        ctx.fill();
    }
}

/** Paints a whole face over skin; the head's texture. */
export function drawFace(
    ctx: CanvasRenderingContext2D,
    doc: FaceDoc,
    skin: string,
    size: number,
    frame: FaceFrame,
    paint?: CanvasImageSource,
) {
    const { x, y, s } = px(size);
    ctx.fillStyle = skin;
    ctx.fillRect(0, 0, size, size);
    if (doc.blush) {
        for (const side of [1, -1]) {
            const g = ctx.createRadialGradient(
                x(0.25 * side),
                y(-0.17),
                0,
                x(0.25 * side),
                y(-0.17),
                s(0.14),
            );
            g.addColorStop(0, `${doc.blush}aa`);
            g.addColorStop(1, `${doc.blush}00`);
            ctx.fillStyle = g;
            ctx.fillRect(0, 0, size, size);
        }
    }
    ctx.fillStyle = 'rgba(70,30,20,.2)';
    ctx.beginPath();
    ctx.ellipse(x(0), y(-0.185), s(0.03), s(0.012), 0, 0, Math.PI * 2);
    ctx.fill();
    for (const side of [1, -1] as const) {
        brow(ctx, doc, skin, size, side);
        eye(ctx, doc, size, side, frame);
    }
    mouth(ctx, doc, size, frame);
    if (doc.freckles) {
        ctx.fillStyle = 'rgba(110,60,40,.55)';
        for (let i = 0; i < 26 * doc.freckles; i++) {
            const a = i * 2.399;
            const r = 0.06 + (i % 7) * 0.025;
            ctx.beginPath();
            ctx.arc(
                x(Math.cos(a) * r * 1.8),
                y(-0.14 + Math.sin(a) * r * 0.5),
                s(0.0065),
                0,
                Math.PI * 2,
            );
            ctx.fill();
        }
    }
    facialHair(ctx, doc, size);
    piercings(ctx, doc, size);
    if (paint) ctx.drawImage(paint, 0, 0, size, size);
}
