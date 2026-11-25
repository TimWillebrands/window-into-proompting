import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { TextArea } from './TextArea';

const meta = {
    title: 'Controls/TextArea',
    component: TextArea,
    decorators: [stage],
    args: { 'aria-label': 'Message', placeholder: 'Say something to the room' },
} satisfies Meta<typeof TextArea>;

export default meta;

type Story = StoryObj<typeof meta>;

const note =
    'Marigold said she would bring the lanterns if Basil handles the playlist. Nobody has asked Juniper yet.';

export const Default: Story = {
    render: (args) => <TextArea {...args} cols={48} defaultValue={note} />,
};

export const Playground: Story = {};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                [
                    'default',
                    <TextArea
                        key="d"
                        aria-label="d"
                        cols={40}
                        rows={3}
                        defaultValue={note}
                    />,
                ],
                [
                    'placeholder',
                    <TextArea
                        key="p"
                        aria-label="p"
                        cols={40}
                        rows={3}
                        placeholder="Say something to the room"
                    />,
                ],
                [
                    'focus',
                    <TextArea
                        key="f"
                        aria-label="f"
                        cols={40}
                        rows={3}
                        defaultValue={note}
                        data-ui-preview="focus"
                    />,
                ],
                [
                    'invalid',
                    <TextArea
                        key="i"
                        aria-label="i"
                        cols={40}
                        rows={3}
                        defaultValue=""
                        invalid
                    />,
                ],
                [
                    'read-only',
                    <TextArea
                        key="r"
                        aria-label="r"
                        cols={40}
                        rows={3}
                        defaultValue={note}
                        readOnly
                    />,
                ],
                [
                    'disabled',
                    <TextArea
                        key="x"
                        aria-label="x"
                        cols={40}
                        rows={3}
                        defaultValue={note}
                        disabled
                    />,
                ],
            ]}
        />
    ),
};

const prompt = `You are Marigold, a lantern-maker who lives above the bakery.
Speak warmly and briefly. Never invent plans for others.
Remember: salience=0.42, mood=curious`;

export const Mono: Story = {
    render: () => (
        <TextArea
            aria-label="System prompt"
            mono
            cols={60}
            rows={4}
            defaultValue={prompt}
        />
    ),
};

export const Noir: Story = { ...AllStates, ...noir };
export const NoirMono: Story = { ...Mono, ...noir };
export const ReducedMotion: Story = { ...AllStates, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...AllStates,
    ...reducedTransparency,
};
