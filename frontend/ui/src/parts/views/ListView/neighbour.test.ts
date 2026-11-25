import { describe, expect, it } from 'vitest';
import { neighbour } from './neighbour';

const cell = (row: number, col: number) => ({
    left: col * 80,
    top: row * 60,
    width: 76,
    height: 56,
});

const grid = [cell(0, 0), cell(0, 1), cell(0, 2), cell(1, 0), cell(1, 1)];

describe('neighbour', () => {
    it('moves along a row', () => {
        expect(neighbour(grid, 0, 'right')).toBe(1);
        expect(neighbour(grid, 1, 'left')).toBe(0);
    });

    it('stops at the end of a row', () => {
        expect(neighbour(grid, 2, 'right')).toBeUndefined();
    });

    it('moves to the closest item in the next row', () => {
        expect(neighbour(grid, 1, 'down')).toBe(4);
        expect(neighbour(grid, 2, 'down')).toBe(4);
        expect(neighbour(grid, 3, 'up')).toBe(0);
    });

    it('walks a single column', () => {
        const column = [cell(0, 0), cell(1, 0), cell(2, 0)];
        expect(neighbour(column, 0, 'down')).toBe(1);
        expect(neighbour(column, 2, 'up')).toBe(1);
        expect(neighbour(column, 1, 'right')).toBeUndefined();
    });
});
