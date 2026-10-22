import type { Meta, StoryObj } from '@storybook/react-vite';
import type { Place } from '../../../api/model/place';
import '../places.css';
import PlaceScene from './PlaceScene';
import type { SceneDef } from './scene';

/** Catalogue prototype (ADR 0027, issue 06): Kenney models next to the primitive prefabs they'd replace. */
const office: Omit<SceneDef, 'id'> = {
    map: `
        ##ww####ww####
        #............#
        #............#
        #............#
        #............#
        D............#
        #............#
        ##############
    `,
    props: [
        { p: 'desk', at: [1, 1], spot: 'old' },
        { p: 'stool', at: [1, 2], rot: 2, spot: 'old' },
        { p: 'big-plant', at: [4, 1] },
        { p: 'k-desk', at: [7, 1], spot: 'new' },
        { p: 'k-desk-chair', at: [7, 2], rot: 2, spot: 'new' },
        { p: 'k-screen', at: [7, 1], y: 0.78 },
        { p: 'k-desk', at: [10, 1] },
        { p: 'k-desk-chair', at: [10, 2], rot: 2, tint: { fabric: '#b33a3a' } },
        { p: 'k-screen', at: [11, 1], y: 0.78 },
    ],
    spots: { old: { at: [2, 3] }, new: { at: [8, 3] } },
    floor: '#b9a27f',
    wall: '#e6dccb',
};

const street: Omit<SceneDef, 'id'> = {
    outdoor: true,
    map: `
        HHHHHHHHHHHHHHHHHH
        ------------------
        ==================
        ------------------
        -....-....-....---
        -....-....-....---
        -....-....-....---
        ------------------
        ==================
        ------------------
    `,
    props: [
        { p: 'k-office-block', at: [1, 4] },
        { p: 'k-low-building', at: [6, 4] },
        { p: 'k-office-block', at: [11, 4] },
        { p: 'street-lamp', at: [5, 3] },
        { p: 'street-lamp', at: [10, 3] },
    ],
    spots: { kerb: { at: [8, 7] } },
    entrances: { '*': [8, 9] },
};

const placeOf = (
    name: string,
    def: Omit<SceneDef, 'id'>,
    spots: string[],
): Place => ({
    id: `story-${name}`,
    name,
    kind: 'public',
    scene: 'custom',
    customScene: JSON.stringify(def),
    spots: spots.map((id) => ({ id, name: id, capacity: 2 })),
});

const officePlace = placeOf('the showroom office', office, ['old', 'new']);
const streetPlace = placeOf('a street in Zuid', street, ['kerb']);

const meta = {
    title: 'Places/Catalogue prototype',
    component: PlaceScene,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 860,
                    height: 520,
                    display: 'flex',
                    flexDirection: 'column',
                }}
            >
                <Story />
            </div>
        ),
    ],
    args: {
        place: officePlace,
        places: [officePlace, streetPlace],
        bodies: [],
        furni: [],
        playerId: null,
        hour: 13,
    },
} satisfies Meta<typeof PlaceScene>;

export default meta;
type Story = StoryObj<typeof meta>;

export const OfficeByDay: Story = {};

export const OfficeAtNight: Story = { args: { hour: 22 } };

export const StreetByDay: Story = { args: { place: streetPlace } };

export const StreetAtNight: Story = { args: { place: streetPlace, hour: 22 } };
