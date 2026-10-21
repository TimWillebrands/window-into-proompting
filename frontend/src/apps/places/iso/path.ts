import type { Pt } from './iso';

const key = (p: Pt) => `${p.x},${p.y}`;

/** 4-neighbour A* on a w×h grid; start and goal are always allowed. Falls back to a straight hop. */
export function findPath(
    from: Pt,
    to: Pt,
    w: number,
    h: number,
    blocked: Set<string>,
): Pt[] {
    const goal = key(to);
    const open: Pt[] = [from];
    const came = new Map<string, Pt>();
    const g = new Map<string, number>([[key(from), 0]]);
    const f = (p: Pt) =>
        (g.get(key(p)) ?? 0) + Math.abs(p.x - to.x) + Math.abs(p.y - to.y);
    while (open.length > 0) {
        open.sort((a, b) => f(a) - f(b));
        const cur = open.shift() as Pt;
        if (key(cur) === goal) {
            const path = [cur];
            let k = key(cur);
            while (came.has(k)) {
                const prev = came.get(k) as Pt;
                path.unshift(prev);
                k = key(prev);
            }
            return path;
        }
        for (const d of [
            { x: 1, y: 0 },
            { x: -1, y: 0 },
            { x: 0, y: 1 },
            { x: 0, y: -1 },
        ]) {
            const n = { x: cur.x + d.x, y: cur.y + d.y };
            const nk = key(n);
            if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= h) continue;
            if (blocked.has(nk) && nk !== goal) continue;
            const cost = (g.get(key(cur)) ?? 0) + 1;
            if (cost < (g.get(nk) ?? Number.POSITIVE_INFINITY)) {
                g.set(nk, cost);
                came.set(nk, cur);
                if (!open.some((o) => key(o) === nk)) open.push(n);
            }
        }
    }
    return [from, to];
}

/** Position along a tile path at progress t ∈ [0, 1], interpolated between tiles. */
export function along(path: Pt[], t: number): Pt {
    if (path.length === 0) return { x: 0, y: 0 };
    if (path.length === 1 || t <= 0) return path[0];
    if (t >= 1) return path[path.length - 1];
    const f = t * (path.length - 1);
    const i = Math.floor(f);
    const a = path[i];
    const b = path[i + 1];
    return { x: a.x + (b.x - a.x) * (f - i), y: a.y + (b.y - a.y) * (f - i) };
}
