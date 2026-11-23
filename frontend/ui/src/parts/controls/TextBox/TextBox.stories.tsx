import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../story-kit';
import { TextBox } from './TextBox';

const meta = {
    title: 'Controls/TextBox',
    component: TextBox,
    decorators: [stage],
    args: { 'aria-label': 'Party name', placeholder: 'Name this Party' },
} satisfies Meta<typeof TextBox>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => <TextBox {...args} defaultValue="Friday night" />,
};

export const Playground: Story = {};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                [
                    'default',
                    <TextBox
                        key="d"
                        aria-label="d"
                        defaultValue="Friday night"
                    />,
                ],
                [
                    'placeholder',
                    <TextBox
                        key="p"
                        aria-label="p"
                        placeholder="Name this Party"
                    />,
                ],
                [
                    'hover',
                    <TextBox
                        key="h"
                        aria-label="h"
                        defaultValue="Friday night"
                        data-ui-preview="hover"
                    />,
                ],
                [
                    'focus',
                    <TextBox
                        key="f"
                        aria-label="f"
                        defaultValue="Friday night"
                        data-ui-preview="focus"
                    />,
                ],
                [
                    'invalid',
                    <TextBox
                        key="i"
                        aria-label="i"
                        defaultValue="!!"
                        invalid
                    />,
                ],
                [
                    'read-only',
                    <TextBox
                        key="r"
                        aria-label="r"
                        defaultValue="party-7f3a"
                        readOnly
                    />,
                ],
                [
                    'disabled',
                    <TextBox
                        key="x"
                        aria-label="x"
                        defaultValue="Friday night"
                        disabled
                    />,
                ],
            ]}
        />
    ),
};

export const Noir: Story = { ...AllStates, parameters: noir };
export const ReducedMotion: Story = { ...AllStates, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...AllStates,
    parameters: reducedTransparency,
};
