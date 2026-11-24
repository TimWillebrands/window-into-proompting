import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { StatusText } from '../../controls/StatusText/StatusText';
import { PropertyList } from './PropertyList';

const meta = {
    title: 'Views/PropertyList',
    component: PropertyList,
    decorators: [stage],
    args: {
        items: [
            { label: 'Address', value: 'http://localhost:11434' },
            { label: 'Model', value: 'llama3.2' },
            {
                label: 'Status',
                value: (
                    <StatusText as="span" tone="go">
                        Ready
                    </StatusText>
                ),
            },
            { label: 'Handles', value: 'General, Character Voice' },
            false,
        ],
    },
} satisfies Meta<typeof PropertyList>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => <PropertyList {...args} className="ui-story-w-320" />,
};

export const Stacked: Story = {
    render: () => (
        <PropertyList
            layout="stacked"
            className="ui-story-w-200"
            items={[
                { label: 'Where', value: 'The Rooftop Bar' },
                { label: 'When', value: '3 Nov, 04:12' },
                {
                    label: 'Remembered by',
                    value: 'Denise, Vlad and somebody with a very long name',
                },
            ]}
        />
    ),
};

export const Noir: Story = { ...Default, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
