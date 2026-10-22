import { describe, expect, it } from 'vitest';
import type { Body } from '../../../api/model/body';
import type { Place } from '../../../api/model/place';
import type { Pt } from '../iso/iso';
import { DOOR_PAUSE_MS, poseOf, STEP_MS, targets } from './motion';
import { entrance, pathIn } from './scene';
import { sceneFor } from './scenes';

const cafe: Place = {
    id: 'cafe',
    name: 'the Café',
    kind: 'public',
    scene: 'cafe',
    spots: [{ id: 'booth', name: 'booth' }],
};
const scene = sceneFor(cafe);
const sceneOf = () => undefined;
const TRIP = 60_000;

const pose = (b: Body, now: number) =>
    poseOf(
        b,
        scene,
        cafe.id,
        sceneOf,
        new Map(),
        new Map<string, Pt>(),
        targets(scene, cafe.id, [b]),
        now,
    );

describe('walking between Places', () => {
    const arriving: Body = {
        participantId: 'a',
        placeId: 'lobby',
        destPlaceId: cafe.id,
        destSpotId: 'booth',
        departedAt: 0,
        arriveAt: TRIP,
    };
    const slot = targets(scene, cafe.id, [arriving]).get('a');
    const door = entrance(scene, undefined, 'lobby');
    const steps = pathIn(scene, door, slot?.tile ?? door).length - 1;

    it('shows up in the doorway, then walks in to reach the spot as the trip ends', () => {
        expect(steps).toBeGreaterThan(2);
        const walkIn = TRIP - steps * STEP_MS;
        expect(pose(arriving, walkIn - DOOR_PAUSE_MS - 50)).toBeNull();
        const inDoor = pose(arriving, walkIn - DOOR_PAUSE_MS / 2);
        expect(inDoor?.moving).toBe(false);
        expect(inDoor?.tile).toEqual(door);
        expect(inDoor?.slot).toBeUndefined();
        const mid = pose(arriving, TRIP - (steps * STEP_MS) / 2);
        expect(mid?.moving).toBe(true);
        const done = pose(arriving, TRIP + 10);
        expect(done?.moving).toBe(false);
        expect(done?.tile).toEqual(slot?.tile);
    });

    it('walks out at a walking pace, pauses in the doorway, then is gone', () => {
        const leaving: Body = {
            participantId: 'a',
            placeId: cafe.id,
            spotId: 'booth',
            destPlaceId: 'lobby',
            departedAt: 0,
            arriveAt: TRIP,
        };
        const out = pathIn(scene, scene.spots.booth.at, door).length - 1;
        expect(pose(leaving, STEP_MS)?.moving).toBe(true);
        expect(pose(leaving, out * STEP_MS - 50)?.moving).toBe(true);
        expect(pose(leaving, out * STEP_MS + 50)?.tile).toEqual(door);
        expect(pose(leaving, out * STEP_MS + DOOR_PAUSE_MS + 50)).toBeNull();
    });
});
