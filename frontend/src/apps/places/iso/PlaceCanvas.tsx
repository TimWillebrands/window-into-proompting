import { useEffect, useMemo, useRef } from 'react';
import { useChatGroupMessages } from '#lib/realtime-store';
import type { Body } from '../../../api/model/body';
import type { FurniView } from '../../../api/model/furniView';
import type { Place } from '../../../api/model/place';
import {
    avatarColour,
    canHear,
    furniIcon,
    scribble,
    toMs,
    volumeOf,
} from '../places-lib';
import { drawHead, drawScarf, lookOf } from './appearance';
import {
    depth,
    type Pt,
    TILE_H,
    TILE_W,
    tileCentre,
    toScreen,
    toTile,
} from './iso';
import { blocked, layoutFor, type RoomLayout, slotTile } from './layout';
import { along, findPath } from './path';

const WALL_H = 72;
const STEP_MS = 260;
const BUBBLE_MS = 14_000;
/** Share of a between-Places walk spent visible in the room you leave / arrive in. */
const DOORWAY = 0.3;

interface Walk {
    path: Pt[];
    startedAt: number;
}

interface Props {
    place: Place;
    bodies: Body[];
    furni: FurniView[];
    playerId: string | null;
    /** Author view: show the words of talk the Player is out of Earshot of. */
    allEars?: boolean;
    /** Clicked a Body (feet to head). */
    onBodyClick?: (participantId: string) => void;
    /** Clicked near a spot; null when the click hit nothing walkable. */
    onSpotClick?: (spotId: string) => void;
}

/** The Habbo-ish room view of one Place. Spots are canonical; every tile here is derived. */
export default function PlaceCanvas({
    place,
    bodies,
    furni,
    playerId,
    allEars = false,
    onSpotClick,
    onBodyClick,
}: Props) {
    const canvasRef = useRef<HTMLCanvasElement>(null);
    const wrapRef = useRef<HTMLDivElement>(null);
    const layout = useMemo(
        () => layoutFor((place.spots ?? []).map((s) => s.id ?? '')),
        [place.spots],
    );
    const messages = useChatGroupMessages(place.talkRoomId ?? '');
    const walks = useRef(new Map<string, Walk>());
    const shown = useRef(new Map<string, Pt>());
    const hover = useRef<Pt | null>(null);
    const feet = useRef(new Map<string, Pt>());
    const latest = useRef({ bodies, furni, messages, playerId, allEars });
    latest.current = { bodies, furni, messages, playerId, allEars };

    // Bodies whose target slot changed start a client-side walk from where they're drawn now.
    useEffect(() => {
        const now = Date.now();
        const block = blocked(layout);
        for (const [id, target] of targets(layout, place.id, bodies)) {
            const from = shown.current.get(id);
            if (!from) {
                shown.current.set(id, target);
                continue;
            }
            const last = walks.current.get(id)?.path.at(-1);
            if (last && last.x === target.x && last.y === target.y) continue;
            const start = { x: Math.round(from.x), y: Math.round(from.y) };
            walks.current.set(id, {
                path: findPath(start, target, layout.w, layout.h, block),
                startedAt: now,
            });
        }
    }, [bodies, layout, place.id]);

    useEffect(() => {
        let raf = 0;
        const draw = () => {
            const canvas = canvasRef.current;
            const wrap = wrapRef.current;
            if (canvas && wrap)
                render(
                    canvas,
                    wrap.clientWidth,
                    layout,
                    place,
                    latest.current,
                    walks.current,
                    shown.current,
                    hover.current,
                    feet.current,
                );
            raf = requestAnimationFrame(draw);
        };
        raf = requestAnimationFrame(draw);
        return () => cancelAnimationFrame(raf);
    }, [layout, place]);

    /** The Body drawn under the pointer (feet to head), if any. */
    const bodyAt = (e: React.MouseEvent<HTMLCanvasElement>): string | null => {
        const canvas = canvasRef.current;
        if (!canvas) return null;
        const rect = canvas.getBoundingClientRect();
        const { scale, origin } = frame(rect.width, layout);
        const x = e.clientX - rect.left - origin.x;
        const y = e.clientY - rect.top - origin.y;
        for (const [id, p] of feet.current)
            if (
                Math.abs(x - p.x) < 12 * scale &&
                y < p.y + 4 * scale &&
                y > p.y - 60 * scale
            )
                return id;
        return null;
    };

    const tileAt = (e: React.MouseEvent<HTMLCanvasElement>): Pt | null => {
        const canvas = canvasRef.current;
        if (!canvas) return null;
        const rect = canvas.getBoundingClientRect();
        const { scale, origin } = frame(rect.width, layout);
        const t = toTile(
            {
                x: e.clientX - rect.left - origin.x,
                y: e.clientY - rect.top - origin.y,
            },
            scale,
        );
        return t.x >= 0 && t.y >= 0 && t.x < layout.w && t.y < layout.h
            ? t
            : null;
    };

    return (
        <div ref={wrapRef} style={{ width: '100%' }}>
            <canvas
                ref={canvasRef}
                style={{
                    display: 'block',
                    width: '100%',
                    cursor: onSpotClick ? 'pointer' : 'default',
                }}
                onMouseMove={(e) => {
                    hover.current = tileAt(e);
                }}
                onMouseLeave={() => {
                    hover.current = null;
                }}
                onClick={(e) => {
                    const body = bodyAt(e);
                    if (body && onBodyClick) {
                        onBodyClick(body);
                        return;
                    }
                    const t = tileAt(e);
                    const spot = t && nearestSpot(layout, t);
                    if (spot && onSpotClick) onSpotClick(spot);
                }}
            />
        </div>
    );
}

/** Where each Body in (or walking within) this Place should stand. */
function targets(
    layout: RoomLayout,
    placeId: string | undefined,
    bodies: Body[],
): Map<string, Pt> {
    const bySpot = new Map<string, Body[]>();
    for (const b of bodies) {
        const spot =
            (b.destPlaceId ?? b.placeId) === placeId
                ? (b.destSpotId ?? b.spotId)
                : undefined;
        if (!spot) continue;
        bySpot.set(spot, [...(bySpot.get(spot) ?? []), b]);
    }
    const out = new Map<string, Pt>();
    for (const [spot, here] of bySpot) {
        const ordered = [...here].sort((a, b) =>
            (a.participantId ?? '').localeCompare(b.participantId ?? ''),
        );
        for (const [i, b] of ordered.entries())
            out.set(b.participantId ?? '', slotTile(layout, spot, i));
    }
    return out;
}

function nearestSpot(layout: RoomLayout, t: Pt): string | null {
    let best: string | null = null;
    let bestD = 3;
    for (const [id, s] of Object.entries(layout.spots)) {
        const d = Math.abs(s.anchor.x - t.x) + Math.abs(s.anchor.y - t.y);
        if (d < bestD) {
            best = id;
            bestD = d;
        }
    }
    return best;
}

function frame(width: number, layout: RoomLayout) {
    const roomW = ((layout.w + layout.h) * TILE_W) / 2;
    const scale = Math.min(1.5, Math.max(0.5, (width - 16) / roomW));
    const origin = {
        x: (layout.h * TILE_W * scale) / 2 + 8,
        y: (WALL_H + 40) * scale,
    };
    const height = origin.y + ((layout.w + layout.h) * TILE_H * scale) / 2 + 16;
    return { scale, origin, height };
}

interface Live {
    bodies: Body[];
    furni: FurniView[];
    messages: ReturnType<typeof useChatGroupMessages>;
    playerId: string | null;
    allEars?: boolean;
}

function render(
    canvas: HTMLCanvasElement,
    width: number,
    layout: RoomLayout,
    place: Place,
    live: Live,
    walks: Map<string, Walk>,
    shown: Map<string, Pt>,
    hover: Pt | null,
    feet: Map<string, Pt>,
) {
    const { scale, origin, height } = frame(width, layout);
    feet.clear();
    const dpr = window.devicePixelRatio || 1;
    if (
        canvas.width !== Math.round(width * dpr) ||
        canvas.height !== Math.round(height * dpr)
    ) {
        canvas.width = Math.round(width * dpr);
        canvas.height = Math.round(height * dpr);
        canvas.style.height = `${height}px`;
    }
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, width, height);
    ctx.save();
    ctx.translate(origin.x, origin.y);
    const now = Date.now();
    const at = (t: Pt) => tileCentre(t, scale);

    drawRoom(ctx, layout, scale, place.kind === 'home', hover);

    type Item = { d: number; draw: () => void };
    const items: Item[] = [];
    const anchors = new Map<string, number>();
    for (const f of live.furni) {
        const spot = layout.spots[f.spotId ?? ''];
        if (!spot) continue;
        const n = anchors.get(f.spotId ?? '') ?? 0;
        anchors.set(f.spotId ?? '', n + 1);
        const p = at(spot.anchor);
        items.push({
            d: depth(spot.anchor) + 0.1,
            draw: () =>
                drawFurni(
                    ctx,
                    f,
                    { x: p.x + (n - 0.5) * 18 * scale, y: p.y },
                    scale,
                ),
        });
    }

    const speakers = new Map<string, Pt>();
    for (const b of live.bodies) {
        const id = b.participantId ?? '';
        const pos = bodyPosition(b, layout, place.id, walks, shown, now);
        if (!pos) continue;
        shown.set(id, pos.tile);
        const p = at(pos.tile);
        speakers.set(id, p);
        feet.set(id, p);
        items.push({
            d: depth(pos.tile) + 0.5,
            draw: () =>
                drawBody(
                    ctx,
                    b,
                    p,
                    scale,
                    pos.moving,
                    now,
                    id === live.playerId,
                ),
        });
    }
    items
        .sort((a, b) => a.d - b.d)
        .forEach((i) => {
            i.draw();
        });

    drawBubbles(ctx, live, speakers, scale, now);
    ctx.restore();
}

function bodyPosition(
    b: Body,
    layout: RoomLayout,
    placeId: string | undefined,
    walks: Map<string, Walk>,
    shown: Map<string, Pt>,
    now: number,
): { tile: Pt; moving: boolean } | null {
    const id = b.participantId ?? '';
    const leaving =
        b.destPlaceId && b.placeId === placeId && b.destPlaceId !== placeId;
    const arriving =
        b.destPlaceId && b.destPlaceId === placeId && b.placeId !== placeId;
    if (leaving || arriving) {
        const start = toMs(b.departedAt);
        const end = toMs(b.arriveAt);
        const p = end > start ? (now - start) / (end - start) : 1;
        const block = blocked(layout);
        if (leaving && p < DOORWAY) {
            const from = shown.get(id) ?? layout.door;
            const path = findPath(
                { x: Math.round(from.x), y: Math.round(from.y) },
                layout.door,
                layout.w,
                layout.h,
                block,
            );
            return { tile: along(path, p / DOORWAY), moving: true };
        }
        if (arriving && p > 1 - DOORWAY) {
            const target = targets(layout, placeId, [b]).get(id) ?? layout.door;
            const path = findPath(
                layout.door,
                target,
                layout.w,
                layout.h,
                block,
            );
            return {
                tile: along(path, (p - (1 - DOORWAY)) / DOORWAY),
                moving: true,
            };
        }
        return null;
    }
    if ((b.destPlaceId ?? b.placeId) !== placeId) return null;
    const walk = walks.get(id);
    if (walk) {
        const total = Math.max(1, walk.path.length - 1) * STEP_MS;
        const t = (now - walk.startedAt) / total;
        if (t < 1) return { tile: along(walk.path, t), moving: true };
        return { tile: walk.path[walk.path.length - 1], moving: false };
    }
    return { tile: shown.get(id) ?? layout.door, moving: false };
}

function diamond(ctx: CanvasRenderingContext2D, t: Pt, scale: number) {
    const s = toScreen(t, scale);
    const hw = (TILE_W * scale) / 2;
    const hh = (TILE_H * scale) / 2;
    ctx.beginPath();
    ctx.moveTo(s.x, s.y);
    ctx.lineTo(s.x + hw, s.y + hh);
    ctx.lineTo(s.x, s.y + 2 * hh);
    ctx.lineTo(s.x - hw, s.y + hh);
    ctx.closePath();
}

function drawRoom(
    ctx: CanvasRenderingContext2D,
    layout: RoomLayout,
    scale: number,
    home: boolean,
    hover: Pt | null,
) {
    const wall = WALL_H * scale;
    // Back walls along the x = 0 and y = 0 edges; the door is a gap in the left wall.
    const corner = toScreen({ x: 0, y: 0 }, scale);
    const right = toScreen({ x: layout.w, y: 0 }, scale);
    const left = toScreen({ x: 0, y: layout.h }, scale);
    ctx.fillStyle = home ? '#c9b79c' : '#9fb6cf';
    ctx.beginPath();
    ctx.moveTo(corner.x, corner.y);
    ctx.lineTo(right.x, right.y);
    ctx.lineTo(right.x, right.y - wall);
    ctx.lineTo(corner.x, corner.y - wall);
    ctx.fill();
    ctx.fillStyle = home ? '#b39f82' : '#86a0bd';
    ctx.beginPath();
    ctx.moveTo(corner.x, corner.y);
    ctx.lineTo(left.x, left.y);
    ctx.lineTo(left.x, left.y - wall);
    ctx.lineTo(corner.x, corner.y - wall);
    ctx.fill();
    const d0 = toScreen(layout.door, scale);
    const d1 = toScreen({ x: 0, y: layout.door.y + 1 }, scale);
    ctx.fillStyle = '#4a3b2a';
    ctx.beginPath();
    ctx.moveTo(d0.x, d0.y);
    ctx.lineTo(d1.x, d1.y);
    ctx.lineTo(d1.x, d1.y - wall * 0.75);
    ctx.lineTo(d0.x, d0.y - wall * 0.75);
    ctx.fill();

    for (let x = 0; x < layout.w; x++)
        for (let y = 0; y < layout.h; y++) {
            diamond(ctx, { x, y }, scale);
            ctx.fillStyle =
                (x + y) % 2 === 0
                    ? home
                        ? '#e8dcc8'
                        : '#d9d4c7'
                    : home
                      ? '#e1d3bc'
                      : '#cfc9bb';
            ctx.fill();
            ctx.strokeStyle = 'rgba(0,0,0,0.08)';
            ctx.stroke();
        }
    if (hover) {
        diamond(ctx, hover, scale);
        ctx.strokeStyle = '#245edb';
        ctx.lineWidth = 2;
        ctx.stroke();
        ctx.lineWidth = 1;
    }
}

function drawFurni(
    ctx: CanvasRenderingContext2D,
    f: FurniView,
    p: Pt,
    scale: number,
) {
    ctx.fillStyle = 'rgba(0,0,0,0.15)';
    ctx.beginPath();
    ctx.ellipse(p.x, p.y + 4 * scale, 16 * scale, 7 * scale, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.font = `${Math.round(28 * scale)}px serif`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillText(furniIcon(f), p.x, p.y + 6 * scale);
    if (f.notable) {
        ctx.font = `${Math.round(14 * scale)}px serif`;
        ctx.fillText('❗', p.x + 14 * scale, p.y - 22 * scale);
    }
}

function drawBody(
    ctx: CanvasRenderingContext2D,
    b: Body,
    p: Pt,
    scale: number,
    moving: boolean,
    now: number,
    isMe: boolean,
) {
    if (!moving && (b.asleep || b.posture === 'Lying')) {
        drawSleeper(ctx, b, p, scale, now);
        return;
    }
    const look = lookOf(b.appearance, avatarColour(b.participantId));
    const sitting = !moving && b.posture === 'Sitting';
    const bob = moving ? Math.abs(Math.sin(now / 90)) * 3 * scale : 0;
    const legs = (sitting ? 6 : 14) * scale;
    const torso = 18 * scale;
    const w = 14 * scale;
    const footY = p.y + 2 * scale - bob;
    ctx.fillStyle = 'rgba(0,0,0,0.25)';
    ctx.beginPath();
    ctx.ellipse(p.x, p.y + 3 * scale, 11 * scale, 5 * scale, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = look.bottom;
    ctx.fillRect(p.x - w / 2, footY - legs, w, legs);
    ctx.fillStyle = look.top;
    ctx.fillRect(
        p.x - w / 2 - 2 * scale,
        footY - legs - torso,
        w + 4 * scale,
        torso,
    );
    drawScarf(ctx, look, p.x, footY - legs - torso, w + 6 * scale, 4 * scale);
    const headY = footY - legs - torso - 8 * scale;
    drawHead(ctx, look, p.x, headY, 8 * scale);
    if (isMe) {
        ctx.beginPath();
        ctx.arc(p.x, headY, 9 * scale, 0, Math.PI * 2);
        ctx.strokeStyle = '#245edb';
        ctx.lineWidth = 2;
        ctx.stroke();
        ctx.lineWidth = 1;
    }
    const busy = b.activity ?? '';
    const icon = busy.includes('phone')
        ? '📱'
        : busy.includes('laptop')
          ? '💻'
          : busy.includes('diary')
            ? '📓'
            : busy
              ? '✋'
              : '';
    if (icon) {
        ctx.font = `${Math.round(13 * scale)}px serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(icon, p.x + 12 * scale, headY + 10 * scale);
    }
    ctx.font = `bold ${Math.max(9, Math.round(10 * scale))}px Tahoma, sans-serif`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.lineWidth = 3;
    ctx.strokeStyle = 'rgba(255,255,255,0.85)';
    const name = isMe ? 'You' : (b.name ?? '').split(' ')[0];
    ctx.strokeText(name, p.x, headY - 10 * scale);
    ctx.fillStyle = '#222';
    ctx.fillText(name, p.x, headY - 10 * scale);
    ctx.lineWidth = 1;
}

function drawSleeper(
    ctx: CanvasRenderingContext2D,
    b: Body,
    p: Pt,
    scale: number,
    now: number,
) {
    ctx.fillStyle = 'rgba(0,0,0,0.2)';
    ctx.beginPath();
    ctx.ellipse(p.x, p.y + 2 * scale, 18 * scale, 6 * scale, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = '#8fa8d8';
    ctx.fillRect(p.x - 16 * scale, p.y - 8 * scale, 32 * scale, 8 * scale);
    const look = lookOf(b.appearance, avatarColour(b.participantId));
    ctx.fillStyle = look.top;
    ctx.fillRect(p.x - 8 * scale, p.y - 11 * scale, 18 * scale, 6 * scale);
    ctx.fillStyle = look.skin;
    ctx.beginPath();
    ctx.arc(p.x - 12 * scale, p.y - 9 * scale, 6 * scale, 0, Math.PI * 2);
    ctx.fill();
    const drift = (now / 60) % 20;
    ctx.globalAlpha = 1 - drift / 20;
    ctx.font = `bold ${Math.round(12 * scale)}px Tahoma, sans-serif`;
    ctx.fillStyle = '#245edb';
    ctx.textAlign = 'center';
    ctx.fillText(
        'z',
        p.x - 10 * scale + drift * 0.6 * scale,
        p.y - 20 * scale - drift * scale,
    );
    ctx.fillText(
        'Z',
        p.x - 4 * scale + drift * 0.6 * scale,
        p.y - 30 * scale - drift * scale,
    );
    ctx.globalAlpha = 1;
}

function drawBubbles(
    ctx: CanvasRenderingContext2D,
    live: Live,
    speakers: Map<string, Pt>,
    scale: number,
    now: number,
) {
    const { bodies } = live;
    const recent = live.messages
        .filter(
            (m) => m.content?.trim() && m.sendAt && now - m.sendAt < BUBBLE_MS,
        )
        .slice(-6);
    const stacked = new Map<string, number>();
    for (const m of [...recent].reverse()) {
        const p = speakers.get(m.senderId);
        if (!p) continue;
        const level = stacked.get(m.senderId) ?? 0;
        stacked.set(m.senderId, level + 1);
        if (level > 1) continue;
        const age = (now - (m.sendAt ?? now)) / BUBBLE_MS;
        const name = (
            bodies.find((b) => b.participantId === m.senderId)?.name ?? ''
        ).split(' ')[0];
        // Out of Earshot: the bubble keeps its shape but not its words (spec § Sound).
        const heard = canHear(m, live.playerId, live.allEars);
        const volume = volumeOf(m);
        const text = clip(
            `${name}: ${heard ? (m.content ?? '') : scribble(m.content)}`,
            70,
        );
        const size = Math.max(
            10,
            Math.round((volume === 'shout' ? 13 : 11) * scale),
        );
        ctx.font = `${volume === 'whisper' ? 'italic ' : volume === 'shout' ? 'bold ' : ''}${size}px Tahoma, sans-serif`;
        const tw = ctx.measureText(text).width;
        const x = p.x - tw / 2 - 6;
        const y = p.y - 78 * scale - level * 22 * scale - age * 30 * scale;
        ctx.globalAlpha = Math.max(0.25, 1 - age * 0.8) * (heard ? 1 : 0.55);
        ctx.fillStyle = heard ? '#fff' : '#eee';
        ctx.strokeStyle = '#555';
        ctx.setLineDash(volume === 'whisper' ? [3, 3] : []);
        ctx.beginPath();
        ctx.roundRect(x, y - 16 * scale, tw + 12, 18 * scale, 6);
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = '#111';
        ctx.textAlign = 'left';
        ctx.textBaseline = 'middle';
        ctx.fillText(text, x + 6, y - 7 * scale);
        ctx.setLineDash([]);
        ctx.globalAlpha = 1;
    }
}

const clip = (s: string, n: number) =>
    s.length > n ? `${s.slice(0, n - 1)}…` : s;
