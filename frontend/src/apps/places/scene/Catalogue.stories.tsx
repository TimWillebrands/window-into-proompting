import type { Meta, StoryObj } from '@storybook/react-vite';
import type { Place } from '../../../api/model/place';
import '../places.css';
import { CATALOGUE } from './catalogue.gen';
import PlaceScene from './PlaceScene';
import type { PropDef, SceneDef } from './scene';

/** Review gallery (ADR 0027): every catalogue model in a row, by day and night; footprints show as their tiles. */
function gallery(): Omit<SceneDef, 'id'> {
    const width = 26;
    const props: PropDef[] = [];
    let x = 1;
    let z = 1;
    for (const [name, prefab] of Object.entries(CATALOGUE)) {
        const [w] = prefab.size ?? [1, 1];
        if (x + w > width - 1) {
            x = 1;
            z += 4;
        }
        props.push({ p: name, at: [x, z] });
        x += w + 1;
    }
    const rows = z + 4;
    const line = (c: string) => c.repeat(width);
    const map = [
        line('#'),
        ...Array.from({ length: rows - 2 }, () => `#${'.'.repeat(width - 2)}#`),
        line('#'),
    ];
    map[rows - 2] = `D${map[rows - 2].slice(1)}`;
    return {
        map: map.join('\n'),
        props,
        spots: { door: { at: [1, rows - 2] } },
        floor: '#b9a27f',
        wall: '#e6dccb',
    };
}

const showroom: Place = {
    id: 'story-catalogue',
    name: 'the catalogue',
    kind: 'public',
    scene: 'custom',
    customScene: JSON.stringify(gallery()),
    spots: [{ id: 'door', name: 'the door', capacity: 2 }],
};

const meta = {
    title: 'Places/Catalogue gallery',
    component: PlaceScene,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 1100,
                    height: 720,
                    display: 'flex',
                    flexDirection: 'column',
                }}
            >
                <Story />
            </div>
        ),
    ],
    args: {
        place: showroom,
        places: [showroom],
        bodies: [],
        furni: [],
        playerId: null,
        hour: 13,
    },
} satisfies Meta<typeof PlaceScene>;

export default meta;
type Story = StoryObj<typeof meta>;

export const ByDay: Story = {};

export const AtNight: Story = { args: { hour: 22 } };
