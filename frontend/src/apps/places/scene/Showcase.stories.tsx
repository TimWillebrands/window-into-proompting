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
