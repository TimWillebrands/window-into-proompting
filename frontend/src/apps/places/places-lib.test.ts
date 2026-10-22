import { describe, expect, it } from 'vitest';
import type { Body } from '../../api/model/body';
import type { Place } from '../../api/model/place';
import {
    awayLines,
    bodiesIn,
    canHear,
    earshotRuns,
    growingSummary,
    mayEnter,
    navigatorPlaces,
    occupancy,
    placeIcon,
    placeLabel,
    scribble,
    serverNow,
    setServerClock,
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

    it('gives Places an icon and an occupancy level', () => {
        expect(placeIcon(cafe, 'me')).toBe('🏢');
        expect(placeIcon({ ...cafe, scene: 'cafe' }, 'me')).toBe('☕');
        expect(
            placeIcon(
                { ...cafe, name: 'the Rooftop Bar', customScene: '{}' },
                'me',
            ),
        ).toBe('🍸');
        expect(placeIcon(myHome, 'me')).toBe('🏠');
        expect(placeIcon(theirHome, 'me')).toBe('🔒');
        const roomy: Place = {
            ...cafe,
            spots: [
                { id: 'a', capacity: 4 },
                { id: 'b', capacity: 6 },
            ],
        };
        expect(occupancy(roomy, 2).level).toBe('calm');
        expect(occupancy(roomy, 5).level).toBe('busy');
        expect(occupancy(roomy, 9).level).toBe('full');
    });

    it('lists Bodies away at a Locale, without repeating a plain workplace', () => {
        const office: Place = {
            id: 'o',
            name: 'the office in Zuid',
            kind: 'locale',
            spots: [],
        };
        const hers: Place = {
            id: 'w',
            name: "Ana's work",
            kind: 'locale',
            spots: [],
        };
        const bodies: Body[] = [
            { name: 'Denise', placeId: 'o', awayAs: 'at work' },
            { name: 'Ana', placeId: 'w', awayAs: 'at work' },
            { name: 'Bo', placeId: 'c' },
        ];
        expect(
            awayLines(bodies, [cafe, office, hers]).map((l) => l.text),
        ).toEqual(['Denise — at work, the office in Zuid', 'Ana — at work']);
    });

    it('sums up a Locale on its way to opening', () => {
        const row = {
            localeId: 'g',
            name: 'the climbing gym',
            district: 'Zuid',
            footfall: '1.5',
            people: 1,
            days: 3,
            missing: 'needs 1 more person',
            contributors: ['Ana'],
            wouldOpenOn: null,
        };
        expect(growingSummary(row, 3)).toEqual({
            name: 'the climbing gym (Zuid)',
            share: 0.5,
            ready: false,
            note: '1p · 3d · needs 1 more person',
            who: 'Ana',
        });
        expect(growingSummary({ ...row, missing: null }, 3).note).toBe(
            'would open',
        );
    });

    it('folds runs of talk out of earshot, keeps what you heard', () => {
        const line = (id: number, senderId: string, heardBy?: string[]) => ({
            messageId: id,
            senderId,
            heardBy,
        });
        const runs = earshotRuns(
            [
                line(1, 'a', ['b']),
                line(2, 'b', ['a']),
                line(3, 'me', ['me']),
                line(4, 'a', ['b']),
                line(5, 'b'),
            ],
            'me',
        );
        expect(runs.map((r) => (r.heard ? 'heard' : r.lines.length))).toEqual([
            2,
            'heard',
            1,
            'heard',
        ]);
        expect(earshotRuns([line(1, 'a', ['b'])], 'me', true)[0].heard).toBe(
            true,
        );
    });

    it('reads the server clock through the skew of the last snapshot', () => {
        setServerClock(10_000, 4_000);
        const t = Date.now();
        expect(serverNow() - t).toBeGreaterThanOrEqual(6_000);
        expect(serverNow() - t).toBeLessThan(6_050);
        setServerClock(0, 0);
    });
});
