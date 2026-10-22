import type { Meta, StoryObj } from '@storybook/react-vite';
import type { Place } from '../../../api/model/place';
import '../places.css';
import { KIT_SCENES } from './kits';
import PlaceScene from './PlaceScene';

/** How each kind of Locale looks once it opens (ADR 0027): kit scenes, spots pinned. */
const opened = (archetype: string): Place => {
    const def = KIT_SCENES[archetype];
    return {
        id: `story-kit-${archetype}`,
        name: archetype,
        kind: 'public',
        scene: 'sketch',
        archetype,
        spots: Object.keys(def.spots).map((id) => ({
            id,
            name: id,
            capacity: 3,
        })),
    };
};

const meta = {
    title: 'Places/Kits',
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
        place: opened('office'),
        places: [],
        bodies: [],
        furni: [],
        playerId: null,
        hour: 13,
    },
} satisfies Meta<typeof PlaceScene>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Office: Story = {};
export const Cafe: Story = { args: { place: opened('cafe') } };
export const Bar: Story = { args: { place: opened('bar'), hour: 22 } };
export const Restaurant: Story = { args: { place: opened('restaurant') } };
export const Shop: Story = { args: { place: opened('shop') } };
export const Gym: Story = { args: { place: opened('gym') } };
export const Park: Story = { args: { place: opened('park') } };
export const Station: Story = { args: { place: opened('station') } };
export const School: Story = { args: { place: opened('school') } };
export const Clinic: Story = { args: { place: opened('clinic') } };
