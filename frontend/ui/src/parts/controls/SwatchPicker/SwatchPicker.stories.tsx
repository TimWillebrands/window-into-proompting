import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { fn } from 'storybook/test';
import {
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { FieldRow } from '..';
import { SwatchPicker } from './SwatchPicker';

const basic = [
    { value: '#ff8080', label: 'Rose' },
    { value: '#ffff80', label: 'Butter' },
    { value: '#80ff80', label: 'Mint' },
    { value: '#00ff80', label: 'Spring' },
    { value: '#80ffff', label: 'Sky' },
    { value: '#0080ff', label: 'Azure' },
    { value: '#ff80c0', label: 'Pink' },
    { value: '#ff80ff', label: 'Orchid' },
    { value: '#804040', label: 'Brick' },
    { value: '#004080', label: 'Navy' },
];

const meta = {
    title: 'Controls/SwatchPicker',
    component: SwatchPicker,
    decorators: [stage],
    args: {
        label: 'Wall colour',
        colours: basic,
        value: '#80ffff',
        noneLabel: 'Default',
        onValueChange: fn(),
    },
} satisfies Meta<typeof SwatchPicker>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Interactive: Story = {
    render: (args) => {
        const [value, setValue] = useState<string | null>(null);
        return (
            <FieldRow label="Walls">
                <SwatchPicker
                    {...args}
                    value={value}
                    onValueChange={setValue}
                />
            </FieldRow>
        );
    },
};

export const NoneChosen: Story = { args: { value: null } };

export const WithoutNone: Story = { args: { noneLabel: undefined } };

export const PlainColours: Story = {
    args: {
        colours: ['#e9dcc4', '#d8e2dc', '#f1d6c4', '#cfd8e6', '#3d405b'],
        value: '#cfd8e6',
    },
};

export const States_: Story = {
    name: 'States',
    render: (args) => (
        <States
            items={[
                ['default', <SwatchPicker key="d" {...args} />],
                [
                    'hover',
                    <SwatchPicker
                        key="h"
                        {...args}
                        className="ui-story-hover-target"
                        value="#004080"
                    />,
                ],
                [
                    'focus',
                    <SwatchPicker
                        key="f"
                        {...args}
                        className="ui-story-focus-target"
                        value="#004080"
                    />,
                ],
                ['disabled', <SwatchPicker key="x" {...args} disabled />],
            ]}
        />
    ),
    play: async (context) => {
        await preview(
            '.ui-story-hover-target .ui-swatch-picker-swatch:first-child',
            'hover',
        )(context);
        await preview(
            '.ui-story-focus-target [tabindex="0"]',
            'focus',
        )(context);
    },
};

export const Noir: Story = { ...noir };
export const ReducedMotion: Story = { ...reducedMotion };
export const ReducedTransparency: Story = { ...reducedTransparency };
