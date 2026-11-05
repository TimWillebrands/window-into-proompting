import { useEffect, useRef, useState } from 'react';
import { type Compiled, compileFigure, type Detail } from './compile';
import type { FigureDoc } from './doc';
import { type FigureObject, figureObject } from './object';
import { loadPrints } from './paint';

const WORKERS = 2;
const KEEP = 48;

interface Job {
    resolve: (c: Compiled) => void;
    reject: (e: Error) => void;
}

const jobs = new Map<number, Job>();
const pool: Worker[] = [];
let next = 0;

function worker(): Worker | null {
    if (typeof Worker === 'undefined') return null;
    if (pool.length < WORKERS) {
        const w = new Worker(new URL('./figure.worker.ts', import.meta.url), {
            type: 'module',
        });
        w.onmessage = (
            e: MessageEvent<{
                id: number;
                compiled?: Compiled;
                error?: string;
            }>,
        ) => {
            const job = jobs.get(e.data.id);
            jobs.delete(e.data.id);
            if (e.data.compiled) job?.resolve(e.data.compiled);
            else job?.reject(new Error(e.data.error ?? 'compile failed'));
        };
        pool.push(w);
        return w;
    }
    return pool[next % pool.length];
}

const cache = new Map<string, Promise<Compiled>>();

/** Mesh buffers for a doc: compiled once, in a worker, however many people wear it. */
export function compiled(doc: FigureDoc, detail: Detail): Promise<Compiled> {
    const key = `${detail.voxel}:${JSON.stringify(doc)}`;
    const hit = cache.get(key);
    if (hit) return hit;
    const id = ++next;
    const w = worker();
    const job = w
        ? new Promise<Compiled>((resolve, reject) => {
              jobs.set(id, { resolve, reject });
              w.postMessage({ id, doc, detail });
          })
        : Promise.resolve().then(() => compileFigure(doc, detail));
    cache.set(key, job);
    if (cache.size > KEEP) cache.delete(cache.keys().next().value as string);
    return job;
}

export const svgsOf = (doc: FigureDoc) =>
    [doc.top, doc.outer, doc.under, doc.bottom]
        .flatMap((g) => g?.prints ?? [])
        .map((p) => p.svg)
        .concat(doc.face.paint ? [doc.face.paint] : []);

export interface Quality extends Detail {
    cloth: number;
    face: number;
}

/** The Figure for a doc; the last one stays up until the next is ready, so a change of clothes doesn't blink. */
export function useFigure(
    doc: FigureDoc,
    quality: Quality,
): FigureObject | null {
    const [figure, setFigure] = useState<FigureObject | null>(null);
    const shown = useRef<FigureObject | null>(null);
    useEffect(() => {
        let gone = false;
        Promise.all([compiled(doc, quality), loadPrints(svgsOf(doc))])
            .then(([c, images]) => {
                if (gone) return;
                const built = figureObject(c, doc, {
                    cloth: quality.cloth,
                    face: quality.face,
                    images,
                });
                shown.current?.dispose();
                shown.current = built;
                setFigure(built);
            })
            .catch((e) => console.warn('Figure failed', e));
        return () => {
            gone = true;
        };
    }, [doc, quality]);
    useEffect(
        () => () => {
            shown.current?.dispose();
            shown.current = null;
        },
        [],
    );
    return figure;
}
