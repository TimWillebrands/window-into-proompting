import type { Meta, StoryObj } from '@storybook/react-vite';
import type { FurniView } from '../../../api/model/furniView';
import type { Place } from '../../../api/model/place';
import '../places.css';
import PlaceScene from './PlaceScene';
import type { SceneDef } from './scene';

/** What a design can be now (ADR 0049): sets, styles, light, ambience, own models, things to do. */
type Def = Omit<SceneDef, 'id'>;

const cellar: Def = {
    map: `
        ###ww###ww##
        #..........#
        #..........#
        w..........#
        #....mmm...#
        #....mmm...#
        D....mmm...#
        #..........#
        ############
    `,
    props: [
        { p: 'record-crates', at: [2, 1], tint: { sleeve: 'teal' } },
        { p: 'lounge-set', at: [5, 4], spot: 'sofas' },
        { p: 'bar-set', at: [7, 1], spot: 'counter' },
        { p: 'neon', at: [5, 1], text: 'VINYL' },
        { p: 'poster', at: [1, 5] },
        { p: 'paper-lantern', at: [6, 3] },
        { p: 'big-plant', at: [10, 7] },
    ],
    furni: {
        'record-crates': { p: 'record-crates', at: [1, 1], spot: 'crates' },
        turntable: { p: 'turntable', at: [8, 1], y: 1.04 },
    },
    spots: {
        crates: { at: [2, 2] },
        sofas: { at: [6, 6] },
        counter: { at: [8, 3] },
    },
    floor: '#8a5a3c',
    wall: '#d9cbb0',
    accent: '#7a2f3d',
    floorStyle: 'planks',
    wallStyle: 'panel',
    light: 'warm',
    ambience: 'dust',
};

const garden: Def = {
    outdoor: true,
    map: `
        HHHHHHHHHHHH
        gggtgggggfff
        g,,,,,,,,ffg
        g,gggggg,ggg
        g,gppppg,gtg
        g,gppppg,ggg
        g,,,,,,,,ggg
        ggg,gggggggg
        hhh,hhhhhhhh
    `,
    entrances: { '*': [3, 8] },
    props: [
        { p: 'bench', at: [4, 3], spot: 'pond' },
        { p: 'reeds', at: [6, 5] },
        { p: 'picnic-table', at: [9, 2], spot: 'picnic' },
        { p: 'string-lights', at: [5, 7] },
        { p: 'lantern', at: [2, 7] },
    ],
    furni: { 'wishing-tree': { p: 'wishing-tree', at: [9, 5], spot: 'tree' } },
    spots: {
        pond: { at: [5, 2] },
        tree: { at: [8, 6] },
        picnic: { at: [8, 3] },
    },
    ambience: 'petals',
};

const club: Def = {
    map: `
        ##############
        #SSSS........#
        #SSSS........#
        #SSSS..kkkk..#
        #......kkkk..#
        #......kkkk..#
        D............#
        #............#
        ##############
    `,
    props: [
        { p: 'drum-kit', at: [1, 1] },
        { p: 'stage-mic', at: [3, 3] },
        { p: 'speaker', at: [4, 1] },
        { p: 'disco-ball', at: [8, 4] },
        { p: 'neon-tube', at: [8, 1] },
        { p: 'neon', at: [11, 1], text: 'LATE' },
        { p: 'bar-set', at: [10, 6], rot: 0, spot: 'bar' },
        { p: 'barrel-table', at: [2, 6] },
        { p: 'totem', at: [12, 1] },
    ],
    models: {
        totem: {
            about: 'a stack of glowing speakers',
            parts: [
                {
                    group: [
                        { box: [0.6, 0.5, 0.5], c: 'charcoal' },
                        {
                            cyl: [0.16, 0.03],
                            at: [0, 0.25, 0.25],
                            rx: 1.5708,
                            c: 'neon-pink',
                            glow: true,
                            tag: 'pulse',
                        },
                    ],
                    array: { n: 3, step: [0, 0.52, 0] },
                },
            ],
            light: { at: [0, 1, 0.4], colour: '#ff4fa3', power: 1.5 },
        },
    },
    spots: {
        stage: { at: [2, 2] },
        floor: { at: [8, 4] },
        bar: { at: [11, 5] },
    },
    floor: '#3a3340',
    wall: '#2b2533',
    accent: '#5b2a6b',
    floorStyle: 'concrete',
    wallStyle: 'brick',
    light: 'neon',
    ambience: 'confetti',
};

const camp: Def = {
    outdoor: true,
    map: `
        hhhhhhhhhhhhhh
        hggggggggggggg
        hgg,,,,ggssss~
        hg,,gg,,gsss~~
        hg,gggg,,ss~~~
        hg,,gg,,gsss~~
        hgg,,,,ggssss~
        hggg,ggggggggg
        hggg,ggggggggg
    `,
    legend: { Q: { ground: 'grass', prop: 'pine' } },
    entrances: { '*': [4, 8] },
    props: [
        { p: 'campfire', at: [4, 4] },
        { p: 'log-seat', at: [4, 3], spot: 'fire' },
        { p: 'log-seat', at: [5, 5], rot: 1, spot: 'fire' },
        { p: 'tent', at: [9, 6] },
        { p: 'pine', at: [1, 1] },
        { p: 'pine', at: [3, 1] },
        { p: 'birch', at: [7, 1] },
        { p: 'pine', at: [11, 1] },
        { p: 'mushrooms', at: [1, 6] },
        { p: 'rock', at: [8, 4], spot: 'shore' },
        { p: 'rowboat', at: [12, 4], rot: 1 },
        { p: 'surfboard', at: [10, 2] },
        { p: 'beach-towel', at: [10, 4], spot: 'shore' },
        { p: 'lantern', at: [8, 7] },
        { p: 'bush', at: [12, 8] },
    ],
    spots: {
        fire: { at: [5, 3] },
        shore: { at: [9, 3] },
        tent: { at: [8, 6] },
    },
    ambience: 'fireflies',
};

/** A design as an agent sent it over MCP: its own models, four things to do. */
const sterrenwacht = {
    map: [
        '####ww##ww####',
        '#SSSSS.......#',
        '#SSSSS.......#',
        'wSSSSS..mmm..#',
        '#.......mmm..#',
        'w.......mmm..#',
        '#............#',
        'D............#',
        '#............#',
        '##############',
    ],
    floor: '#5a4634',
    wall: '#2f3550',
    accent: '#27406b',
    floorStyle: 'planks',
    wallStyle: 'panel',
    light: 'dim',
    ambience: 'dust',
    models: {
        'great-telescope': {
            about: 'a big brass refractor on a fork mount',
            size: [2, 2],
            parts: [
                {
                    cyl: [0.5, 0.6, 0.22],
                    at: [0, 0, 0],
                    c: 'dark-wood',
                    sides: 12,
                },
                {
                    ring: [0.52, 0.03],
                    at: [0, 0.22, 0],
                    rx: 1.5708,
                    c: 'brass',
                },
                {
                    box: [0.12, 1.0, 0.16],
                    at: [0.36, 0.22, 0],
                    c: 'iron',
                    mirror: 'x',
                },
                { cyl: [0.05, 0.84], at: [0, 1.1, 0], rz: 1.5708, c: 'iron' },
                {
                    group: [
                        {
                            cyl: [0.19, 0.23, 1.5],
                            at: [0, -0.6, 0],
                            c: 'brass',
                            sides: 12,
                        },
                        {
                            cyl: [0.26, 0.26, 0.1],
                            at: [0, 0.86, 0],
                            c: 'copper',
                            sides: 12,
                        },
                        {
                            cyl: [0.2, 0.02],
                            at: [0, 0.95, 0],
                            c: 'glass',
                            glow: 'night',
                            sides: 12,
                        },
                        { cyl: [0.07, 0.3], at: [0, -0.88, 0], c: 'black' },
                        { cyl: [0.045, 0.6], at: [0.26, -0.2, 0], c: 'copper' },
                        {
                            ring: [0.24, 0.02],
                            at: [0, 0.2, 0],
                            rx: 1.5708,
                            c: 'copper',
                        },
                    ],
                    at: [0, 1.12, 0],
                    rx: -0.75,
                },
                { ball: 0.1, at: [0.5, 1.1, 0], c: 'iron' },
            ],
        },
        orrery: {
            about: 'a brass orrery with a glowing sun',
            parts: [
                {
                    lathe: [
                        [0.26, 0],
                        [0.1, 0.08],
                        [0.06, 0.62],
                        [0.18, 0.72],
                        [0.2, 0.76],
                    ],
                    c: 'dark-wood',
                    sides: 10,
                },
                {
                    ball: 0.11,
                    at: [0, 1.0, 0],
                    c: 'amber',
                    glow: true,
                    tag: 'pulse',
                },
                { cyl: [0.015, 0.26], at: [0, 0.76, 0], c: 'brass' },
                {
                    group: [
                        {
                            box: [0.42, 0.012, 0.012],
                            at: [0.21, 0, 0],
                            c: 'brass',
                        },
                        { ball: 0.05, at: [0.42, 0.02, 0], c: 'blue' },
                        { ball: 0.018, at: [0.42, 0.02, 0.09], c: 'white' },
                        {
                            box: [0.28, 0.012, 0.012],
                            at: [0.14, 0.05, 0],
                            ry: 2.2,
                            c: 'brass',
                        },
                        { ball: 0.035, at: [-0.165, 0.07, -0.226], c: 'rust' },
                        {
                            box: [0.34, 0.012, 0.012],
                            at: [0.17, -0.05, 0],
                            ry: 4.1,
                            c: 'brass',
                        },
                        { ball: 0.06, at: [-0.195, -0.03, 0.278], c: 'sand' },
                        {
                            ring: [0.09, 0.008],
                            at: [-0.195, -0.03, 0.278],
                            rx: 1.3,
                            c: 'gold',
                        },
                    ],
                    at: [0, 1.0, 0],
                    tag: 'spin',
                },
            ],
            light: { at: [0, 1.1, 0], colour: '#ffc66b', power: 1.4 },
        },
        'star-chart': {
            about: 'a wall chart of the northern sky',
            wall: true,
            parts: [
                {
                    box: [0.94, 0.94, 0.03],
                    at: [0, 0.95, -0.47],
                    c: 'dark-wood',
                },
                {
                    cyl: [0.42, 0.02],
                    at: [0, 1.42, -0.445],
                    rx: 1.5708,
                    c: 'navy',
                    sides: 20,
                },
                { ring: [0.3, 0.006], at: [0, 1.42, -0.43], c: 'sky' },
                { ring: [0.16, 0.006], at: [0, 1.42, -0.43], c: 'sky' },
                {
                    ball: 0.018,
                    at: [0.1, 1.5, -0.43],
                    c: 'warm-light',
                    glow: true,
                    tag: 'twinkle',
                },
                {
                    ball: 0.014,
                    at: [-0.2, 1.6, -0.43],
                    c: 'white',
                    glow: true,
                    tag: 'twinkle',
                },
                {
                    ball: 0.016,
                    at: [0.24, 1.3, -0.43],
                    c: 'white',
                    glow: true,
                    tag: 'twinkle',
                },
                {
                    ball: 0.012,
                    at: [-0.12, 1.25, -0.43],
                    c: 'warm-light',
                    glow: true,
                    tag: 'twinkle',
                },
                {
                    ball: 0.014,
                    at: [-0.28, 1.42, -0.43],
                    c: 'white',
                    glow: true,
                    tag: 'twinkle',
                },
                { ball: 0.02, at: [0, 1.42, -0.43], c: 'amber', glow: true },
                {
                    tube: [
                        [0.1, 1.5, -0.432],
                        [0.24, 1.3, -0.432],
                        [-0.12, 1.25, -0.432],
                    ],
                    r: 0.004,
                    c: 'sky',
                },
            ],
        },
    },
    props: [
        {
            p: 'great-telescope',
            at: [2, 1],
            name: 'the great telescope',
            spot: 'telescope',
            do: 'Look through the great telescope',
            gives: "the one thing in the eyepiece tonight: a named star, a planet's moons counted, a lit window across the canal, depending on the hour and the weather",
        },
        {
            p: 'guestbook',
            at: [5, 3],
            name: 'the observation log',
            spot: 'telescope',
            do: "Write tonight's sighting in the observation log",
            gives: 'the line they enter: what they saw, the time, and one remark in their own voice',
            leaves: true,
        },
        {
            p: 'orrery',
            at: [6, 6],
            name: 'the orrery',
            spot: 'orrery',
            do: 'Wind up the orrery',
            gives: "which planet catches up with which as it runs down, and the small thing that's wrong with the model",
        },
        {
            p: 'star-chart',
            at: [7, 1],
            name: 'the star chart',
            spot: 'shelves',
            do: "Find tonight's sky on the star chart",
            gives: 'the constellation overhead at this hour and the note someone pencilled beside it',
        },
        { p: 'star-chart', at: [9, 1] },
        { p: 'library-shelves', at: [11, 1], spot: 'shelves' },
        { p: 'library-shelves', at: [12, 1] },
        {
            p: 'lounge-set',
            at: [8, 3],
            spot: 'armchairs',
            tint: { rug: 'navy' },
        },
        { p: 'chandelier', at: [6, 4] },
        { p: 'globe', at: [1, 8] },
        { p: 'desk-set', at: [11, 6] },
        { p: 'candles', at: [9, 4], y: 0.36 },
        { p: 'curtain', at: [1, 4], tint: { cloth: 'navy' } },
        { p: 'big-plant', at: [12, 8] },
    ],
    spots: {
        telescope: { at: [4, 2] },
        armchairs: { at: [9, 5] },
        orrery: { at: [6, 7] },
        shelves: { at: [11, 2] },
    },
} as unknown as Def;

const placeOf = (id: string, def: Def, spots: string[]): Place => ({
    id: `story-${id}`,
    name: id,
    kind: 'public',
    scene: 'custom',
    customScene: JSON.stringify(def),
    spots: spots.map((s) => ({ id: s, name: s, capacity: 3 })),
});

const things = (place: Place, def: Def): FurniView[] =>
    Object.keys(def.furni ?? {}).map(
        (id) =>
            ({
                placeId: place.id,
                furniId: id,
                kind: 'feature',
                name: id,
                condition: 1,
                useLabel: 'Do it',
                gives: 'something',
            }) as FurniView,
    );

const cellarPlace = placeOf('cellar', cellar, ['crates', 'sofas', 'counter']);
const gardenPlace = placeOf('garden', garden, ['pond', 'tree', 'picnic']);
const clubPlace = placeOf('club', club, ['stage', 'floor', 'bar']);
const campPlace = placeOf('camp', camp, ['fire', 'shore', 'tent']);
const sterrenwachtPlace = placeOf(
    'sterrenwacht',
    {
        ...sterrenwacht,
        // The server stores the rows joined.
        map: (sterrenwacht.map as unknown as string[]).join('\n'),
    },
    ['telescope', 'armchairs', 'orrery', 'shelves'],
);

const meta = {
    title: 'Places/Showcase',
    component: PlaceScene,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 1000,
                    height: 640,
                    display: 'flex',
                    flexDirection: 'column',
                }}
            >
                <Story />
            </div>
        ),
    ],
    args: {
        place: cellarPlace,
        places: [cellarPlace, gardenPlace, clubPlace, campPlace],
        bodies: [],
        furni: things(cellarPlace, cellar),
        playerId: null,
        hour: 14,
    },
} satisfies Meta<typeof PlaceScene>;

export default meta;
type Story = StoryObj<typeof meta>;

export const GrooveCellar: Story = {};
export const WishingGarden: Story = {
    args: { place: gardenPlace, furni: things(gardenPlace, garden) },
};
export const WishingGardenAtDusk: Story = {
    args: { place: gardenPlace, furni: things(gardenPlace, garden), hour: 21 },
};
export const Club: Story = { args: { place: clubPlace, furni: [] } };
export const Camp: Story = { args: { place: campPlace, furni: [] } };
export const CampAtNight: Story = {
    args: { place: campPlace, furni: [], hour: 23 },
};
export const Sterrenwacht: Story = {
    args: { place: sterrenwachtPlace, furni: [], hour: 22 },
};
export const SterrenwachtByDay: Story = {
    args: { place: sterrenwachtPlace, furni: [], hour: 13 },
};
