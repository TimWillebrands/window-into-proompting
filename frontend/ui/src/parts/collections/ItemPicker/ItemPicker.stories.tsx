import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { fn } from 'storybook/test';
import { preview } from '../../chrome/story-kit';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../controls/story-kit';
import { Icon } from '../../icons';
import { ItemPicker, type ItemPickerItem } from './ItemPicker';

const items: ItemPickerItem[] = [
    {
        id: 'tee',
        label: 'Navy band tee',
        picture: '👕',
        swatch: '#2c3e66',
        detail: '12¢',
        badge: 'wearing',
        badgeTone: 'go',
    },
    {
        id: 'jeans',
        label: 'Baggy jeans',
        picture: '👖',
        swatch: '#4a7fd6',
        detail: '18¢',
        badge: 'owned',
    },
    {
        id: 'boots',
        label: 'Platform boots',
        picture: '🥾',
        swatch: '#2b2b2b',
        detail: '40¢',
        detailTone: 'danger',
    },
    {
        id: 'cut',
        label: 'A pixie cut at the salon',
        picture: '✂️',
        swatch: '#ec8fb5',
        detail: '25¢',
        tag: 'salon',
    },
    {
        id: 'gift',
        label: 'Gift wrap',
        picture: <Icon name="gift" size={32} />,
        detail: 'free',
    },
    {
        id: 'gone',
        label: 'Sold out',
        picture: '🧣',
        swatch: '#6e2233',
        disabled: true,
    },
];

const meta = {
    title: 'Collections/ItemPicker',
    component: ItemPicker,
    decorators: [stage],
    parameters: { stage: { width: 'wide' } },
    args: {
        label: 'Shop today',
        items,
        selected: ['jeans'],
        onToggle: fn(),
    },
} satisfies Meta<typeof ItemPicker>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Interactive: Story = {
    render: (args) => {
        const [on, setOn] = useState<string[]>([]);
        return (
            <ItemPicker
                {...args}
                selected={on}
                onToggle={(id) =>
                    setOn((x) =>
                        x.includes(id) ? x.filter((y) => y !== id) : [...x, id],
                    )
                }
            />
        );
    },
};

export const Small: Story = {
    args: {
        size: 'small',
        label: 'Trying on',
        items: items.slice(0, 4).map((item) => ({
            ...item,
            hint: `Take off ${item.label}`,
        })),
        selected: items.slice(0, 4).map((item) => item.id),
    },
};

export const Empty: Story = {
    args: { items: [], empty: 'Sold out!' },
};

export const States_: Story = {
    name: 'States',
    render: (args) => (
        <States
            items={[
                [
                    'hover',
                    <ItemPicker
                        key="h"
                        {...args}
                        className="ui-story-hover-target"
                        items={items.slice(0, 2)}
                        selected={[]}
                    />,
                ],
                [
                    'focus',
                    <ItemPicker
                        key="f"
                        {...args}
                        className="ui-story-focus-target"
                        items={items.slice(0, 2)}
                        selected={[]}
                    />,
                ],
                [
                    'pressed',
                    <ItemPicker
                        key="p"
                        {...args}
                        items={items.slice(0, 2)}
                        selected={['tee']}
                    />,
                ],
                [
                    'disabled',
                    <ItemPicker
                        key="d"
                        {...args}
                        items={items.slice(5)}
                        selected={[]}
                    />,
                ],
            ]}
        />
    ),
    play: async (context) => {
        await preview(
            '.ui-story-hover-target .ui-item-picker-item:first-child',
            'hover',
        )(context);
        await preview(
            '.ui-story-focus-target .ui-item-picker-item:first-child',
            'focus',
        )(context);
    },
};

export const Noir: Story = { parameters: noir };
export const ReducedMotion: Story = { parameters: reducedMotion };
export const ReducedTransparency: Story = { parameters: reducedTransparency };
