export type Side = 'above' | 'below' | 'start' | 'end';

export type Point = { x: number; y: number };

export type Rect = {
    left: number;
    top: number;
    right: number;
    bottom: number;
    width: number;
    height: number;
};

export type Size = { width: number; height: number };

const margin = 4;

const viewport = (): Size =>
    typeof window === 'undefined'
        ? { width: 1024, height: 768 }
        : { width: window.innerWidth, height: window.innerHeight };

const clamp = (value: number, min: number, max: number) =>
    Math.max(min, Math.min(value, Math.max(min, max)));

export function placeAtPoint(point: Point, size: Size): Point {
    const view = viewport();
    const x =
        point.x + size.width + margin > view.width
            ? point.x - size.width
            : point.x;
    const y =
        point.y + size.height + margin > view.height
            ? point.y - size.height
            : point.y;
    return {
        x: clamp(x, margin, view.width - size.width - margin),
        y: clamp(y, margin, view.height - size.height - margin),
    };
}

export function placeBeside(anchor: Rect, size: Size, gap = 0): Point {
    const view = viewport();
    const fitsEnd = anchor.right + gap + size.width + margin <= view.width;
    const x = fitsEnd ? anchor.right + gap : anchor.left - gap - size.width;
    const y =
        anchor.top + size.height + margin > view.height
            ? anchor.bottom - size.height
            : anchor.top;
    return {
        x: clamp(x, margin, view.width - size.width - margin),
        y: clamp(y, margin, view.height - size.height - margin),
    };
}

export type Placed = Point & { side: 'above' | 'below'; tip: number };

export function placeAround(
    anchor: Rect,
    size: Size,
    preferred: 'above' | 'below',
    gap: number,
): Placed {
    const view = viewport();
    const roomAbove = anchor.top - gap - size.height >= margin;
    const roomBelow = anchor.bottom + gap + size.height + margin <= view.height;
    const side =
        preferred === 'above'
            ? roomAbove || !roomBelow
                ? 'above'
                : 'below'
            : roomBelow || !roomAbove
              ? 'below'
              : 'above';
    const centre = anchor.left + anchor.width / 2;
    const x = clamp(
        centre - size.width / 2,
        margin,
        view.width - size.width - margin,
    );
    const y =
        side === 'above' ? anchor.top - gap - size.height : anchor.bottom + gap;
    return {
        x,
        y: clamp(y, margin, view.height - size.height - margin),
        side,
        tip: centre - x,
    };
}
