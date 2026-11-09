import type { Meta, StoryObj } from '@storybook/react-vite';
import type { Place } from '../../../api/model/place';
import '../places.css';
import PlaceScene from './PlaceScene';
import { LIBRARY, PREFABS } from './prefabs';
import type { PropDef, SceneDef } from './scene';

/** Review gallery (ADR 0049): every library model on its footprint, in rows. */
function gallery(names: string[], width = 24): Omit<SceneDef, 'id'> {
    const props: PropDef[] = [];
    let x = 1;
    let z = 1;
    let deep = 1;
    for (const name of names) {
        const [w, d] = PREFABS[name]?.size ?? [1, 1];
        if (x + w > width - 1) {
            x = 1;
            z += deep + 2;
            deep = 1;
        }
        props.push({ p: name, at: [x, z] });
        deep = Math.max(deep, d);
        x += w + 1;
    }
    const rows = z + deep + 2;
    const map = Array.from({ length: rows }, (_, r) =>
        r === 0
            ? '#'.repeat(width)
            : `#${(r % 2 ? '.' : '.').repeat(width - 2)}#`,
    );
    map[rows - 1] = '#'.repeat(width);
    map[rows - 2] = `D${map[rows - 2].slice(1)}`;
    return {
        map: map.join('\n'),
        props,
        spots: { door: { at: [1, rows - 2] } },
        floor: '#b9a27f',
        wall: '#e6dccb',
    };
}

const placeOf = (id: string, def: Omit<SceneDef, 'id'>): Place => ({
    id: `story-${id}`,
    name: id,
    kind: 'public',
    scene: 'custom',
    customScene: JSON.stringify(def),
    spots: [{ id: 'door', name: 'the door', capacity: 2 }],
});

const half = Math.ceil(LIBRARY.length / 2);
const first = placeOf('library-a', gallery(LIBRARY.slice(0, half)));
const second = placeOf('library-b', gallery(LIBRARY.slice(half)));

const meta = {
    title: 'Places/Library',
    component: PlaceScene,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 1400,
                    height: 900,
                    display: 'flex',
                    flexDirection: 'column',
                }}
            >
                <Story />
            </div>
        ),
    ],
    args: {
        place: first,
        places: [first, second],
        bodies: [],
        furni: [],
        playerId: null,
        hour: 13,
    },
} satisfies Meta<typeof PlaceScene>;

export default meta;
type Story = StoryObj<typeof meta>;

export const FirstHalf: Story = {};
export const SecondHalf: Story = { args: { place: second } };
export const FirstHalfAtNight: Story = { args: { hour: 22 } };
export const SecondHalfAtNight: Story = { args: { place: second, hour: 22 } };
