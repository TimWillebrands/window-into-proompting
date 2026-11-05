import { describe, expect, it } from 'vitest';
import { lookOf } from '../iso/appearance';
import { compileFigure, SCENE_DETAIL } from './compile';
import { figureDoc } from './dress';
import { seededAppearance } from './portrait';

describe('seededAppearance', () => {
    it('gives a seed the same look every time', () => {
        expect(seededAppearance('Sophie')).toEqual(seededAppearance('Sophie'));
    });

    it('tells people apart', () => {
        const looks = ['jhon', 'mary', 'rebeca', 'michael', 'marina', 'sophie'];
        const seen = new Set(
            looks.map((s) => JSON.stringify(seededAppearance(s))),
        );
        expect(seen.size).toBe(looks.length);
    });

    it('compiles to a Figure', () => {
        const doc = figureDoc(lookOf(seededAppearance('mary'), '#d9534f'));
        expect(compileFigure(doc, SCENE_DETAIL).layers.length).toBeGreaterThan(
            0,
        );
    });
});
