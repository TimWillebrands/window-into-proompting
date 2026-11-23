export type Rgba = [number, number, number, number];

export type Stop = { color: Rgba; at: number };

const colorPattern = /rgba?\(([^)]+)\)/g;

const positionPattern = /calc\(100% - ([\d.]+)px\)|(-?[\d.]+)(%|px)/g;

const parseChannels = (body: string): Rgba => {
    const parts = body
        .replace('/', ' ')
        .split(/[\s,]+/)
        .filter(Boolean)
        .map(Number);
    return [parts[0], parts[1], parts[2], parts[3] ?? 1];
};

export const parseColor = (value: string): Rgba | undefined => {
    const match = /rgba?\(([^)]+)\)/.exec(value);
    return match ? parseChannels(match[1]) : undefined;
};

const positionsIn = (segment: string, height: number) =>
    [...segment.matchAll(positionPattern)].map((m) => {
        if (m[1] !== undefined) return 100 - (Number(m[1]) / height) * 100;
        return m[3] === '%' ? Number(m[2]) : (Number(m[2]) / height) * 100;
    });

export const parseStops = (image: string, height = 100): Stop[] => {
    const layer = image.slice(Math.max(0, image.lastIndexOf('gradient(')));
    const colors = [...layer.matchAll(colorPattern)];
    const found = colors.flatMap((m, i) => {
        const from = (m.index ?? 0) + m[0].length;
        const to = colors[i + 1]?.index ?? layer.length;
        const color = parseChannels(m[1]);
        const at = positionsIn(layer.slice(from, to), Math.max(1, height));
        return at.length > 0
            ? at.map((position) => ({ color, at: position }))
            : [{ color, at: Number.NaN }];
    });
    return found.map((stop, i) => ({
        ...stop,
        at: Number.isNaN(stop.at)
            ? (i / Math.max(1, found.length - 1)) * 100
            : stop.at,
    }));
};

const mix = (a: Rgba, b: Rgba, t: number): Rgba => [
    a[0] + (b[0] - a[0]) * t,
    a[1] + (b[1] - a[1]) * t,
    a[2] + (b[2] - a[2]) * t,
    a[3] + (b[3] - a[3]) * t,
];

const colorAt = (stops: Stop[], at: number): Rgba => {
    if (at <= stops[0].at) return stops[0].color;
    for (let i = 1; i < stops.length; i++) {
        const prev = stops[i - 1];
        const next = stops[i];
        if (at <= next.at) {
            const span = next.at - prev.at;
            return mix(
                prev.color,
                next.color,
                span ? (at - prev.at) / span : 0,
            );
        }
    }
    return stops[stops.length - 1].color;
};

export const bandColors = (stops: Stop[], from = 25, to = 75): Rgba[] => {
    if (stops.length === 0) return [];
    const inside = stops.filter((s) => s.at > from && s.at < to);
    return [
        colorAt(stops, from),
        colorAt(stops, (from + to) / 2),
        colorAt(stops, to),
        ...inside.map((s) => s.color),
    ];
};

const over = (top: Rgba, under: Rgba): Rgba => {
    const a = top[3];
    return [
        top[0] * a + under[0] * (1 - a),
        top[1] * a + under[1] * (1 - a),
        top[2] * a + under[2] * (1 - a),
        1,
    ];
};

const channel = (v: number) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
};

const luminance = ([r, g, b]: Rgba) =>
    0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);

export const ratio = (text: Rgba, surface: Rgba) => {
    const solidSurface = over(surface, [255, 255, 255, 1]);
    const solidText = over(text, solidSurface);
    const [hi, lo] = [luminance(solidText), luminance(solidSurface)].sort(
        (a, b) => b - a,
    );
    return (hi + 0.05) / (lo + 0.05);
};

export const worstRatio = (el: HTMLElement) => {
    const style = getComputedStyle(el);
    const text = parseColor(style.color);
    if (!text) return undefined;
    const stops = parseStops(
        style.backgroundImage,
        el.getBoundingClientRect().height,
    );
    const surfaces =
        stops.length > 0
            ? bandColors(stops)
            : [parseColor(style.backgroundColor)].filter(
                  (c): c is Rgba => c !== undefined,
              );
    if (surfaces.length === 0) return undefined;
    return Math.min(...surfaces.map((s) => ratio(text, s)));
};
