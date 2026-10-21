import { describe, expect, it } from 'vitest';
import type { Body } from '../../api/model/body';
import type { Place } from '../../api/model/place';
import {
    bodiesIn,
    mayEnter,
    navigatorPlaces,
    placeLabel,
    walkingText,
} from './places-lib';

const cafe: Place = { id: 'c', name: 'the Café', kind: 'public', spots: [] };
const myHome: Place = {
    id: 'h1',
    name: "Tim's place",
    kind: 'home',
    ownerId: 'me',
    spots: [],
};
const theirHome: Place = {
    id: 'h2',
    name: "Hana's place",
    kind: 'home',
    ownerId: 'hana',
    spots: [],
};

describe('places-lib', () => {
    it('counts a walking Body at its destination', () => {
        const walking: Body = {
            participantId: 'a',
            placeId: 'h1',
            destPlaceId: 'c',
        };
        const sitting: Body = { participantId: 'b', placeId: 'c' };
        expect(bodiesIn([walking, sitting], 'c')).toHaveLength(2);
        expect(bodiesIn([walking, sitting], 'h1')).toHaveLength(0);
    });

    it('labels your own Home as Home and capitalises public Places', () => {
        expect(placeLabel(myHome, 'me')).toBe('Home');
        expect(placeLabel(cafe, 'me')).toBe('Café');
        expect(placeLabel(theirHome, 'me')).toBe("Hana's place");
    });

    it('hides other people’s Homes from the Navigator', () => {
        expect(mayEnter(theirHome, 'me')).toBe(false);
        expect(
            navigatorPlaces([theirHome, myHome, cafe], 'me').map((p) => p.id),
        ).toEqual(['c', 'h1']);
    });

    it('counts down while walking', () => {
        const b: Body = { destPlaceId: 'c', arriveAt: 10_000 };
        expect(walkingText(b, 0)).toBe('arriving in 10s');
        expect(walkingText(b, 20_000)).toBe('arriving…');
        expect(walkingText({ placeId: 'c' }, 0)).toBeNull();
    });
});
