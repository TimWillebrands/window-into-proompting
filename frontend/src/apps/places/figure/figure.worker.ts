import { compileFigure, type Detail } from './compile';
import type { FigureDoc } from './doc';

/** Compiles Figures off the main thread: fields and surface nets are the expensive part. */
self.onmessage = (
    e: MessageEvent<{ id: number; doc: FigureDoc; detail: Detail }>,
) => {
    const { id, doc, detail } = e.data;
    try {
        const compiled = compileFigure(doc, detail);
        const transfer = compiled.layers.flatMap((l) =>
            [
                l.positions,
                l.normals,
                l.indices,
                l.skinIndex,
                l.skinWeight,
                l.colors,
                l.uvs,
            ]
                .filter((b) => !!b)
                .map((b) => b.buffer as ArrayBuffer),
        );
        (self as unknown as Worker).postMessage({ id, compiled }, transfer);
    } catch (error) {
        (self as unknown as Worker).postMessage({ id, error: String(error) });
    }
};
