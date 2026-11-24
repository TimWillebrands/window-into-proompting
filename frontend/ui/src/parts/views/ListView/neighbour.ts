export type Direction = 'up' | 'down' | 'left' | 'right';

export type Box = Pick<DOMRect, 'left' | 'top' | 'width' | 'height'>;

export function neighbour(
    rects: ReadonlyArray<Box>,
    from: number,
    direction: Direction,
): number | undefined {
    const origin = rects[from];
    if (!origin) return undefined;
    const ox = origin.left + origin.width / 2;
    const oy = origin.top + origin.height / 2;
    const vertical = direction === 'up' || direction === 'down';
    const sign = direction === 'down' || direction === 'right' ? 1 : -1;
    let best: number | undefined;
    let bestPrimary = Number.POSITIVE_INFINITY;
    let bestSecondary = Number.POSITIVE_INFINITY;
    rects.forEach((rect, index) => {
        if (index === from) return;
        const x = rect.left + rect.width / 2;
        const y = rect.top + rect.height / 2;
        const primary = (vertical ? y - oy : x - ox) * sign;
        if (primary < 1) return;
        const secondary = Math.abs(vertical ? x - ox : y - oy);
        const rounded = Math.round(primary / 2);
        const bestRounded = Math.round(bestPrimary / 2);
        if (
            rounded < bestRounded ||
            (rounded === bestRounded && secondary < bestSecondary)
        ) {
            best = index;
            bestPrimary = primary;
            bestSecondary = secondary;
        }
    });
    return best;
}
