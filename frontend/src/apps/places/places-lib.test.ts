import { describe, expect, it } from 'vitest';
import type { Body } from '../../api/model/body';
import type { Place } from '../../api/model/place';
import {
    bodiesIn,
    canHear,
    mayEnter,
    navigatorPlaces,
    placeLabel,
    scribble,
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

    it('lists your Home before other people’s, which you can’t just walk into', () => {
        expect(mayEnter(theirHome, 'me')).toBe(false);
        expect(mayEnter({ ...theirHome, guests: ['me'] }, 'me')).toBe(true);
        expect(
            navigatorPlaces([theirHome, myHome, cafe], 'me').map((p) => p.id),
        ).toEqual(['c', 'h1', 'h2']);
    });

    it('counts down while walking', () => {
        const b: Body = { destPlaceId: 'c', arriveAt: 10_000 };
        expect(walkingText(b, 0)).toBe('arriving in 10s');
        expect(walkingText(b, 20_000)).toBe('arriving…');
        expect(walkingText({ placeId: 'c' }, 0)).toBeNull();
    });

    it('hides the words of talk you were out of Earshot of', () => {
        expect(canHear({ heardBy: null }, 'me')).toBe(true);
        expect(canHear({ heardBy: ['you'] }, 'me')).toBe(false);
        expect(canHear({ heardBy: ['you'] }, 'me', true)).toBe(true);
        expect(scribble('honestly Pieter has been impossible')).toBe(
            '~~~~ ~~~ ~~ ~~ ~~~~~',
        );
    });
});
