import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import PeoplePicker, { type PeoplePickerPerson } from './PeoplePicker';

const samplePeople: PeoplePickerPerson[] = [
    { id: 'persona-1', name: 'Indie Hacker Girl', subtitle: 'Denise' },
    { id: 'persona-2', name: 'Shaman Agile Coach', subtitle: 'Vlad' },
    { id: 'persona-3', name: 'CTO Skeptic' },
    {
        id: 'persona-4',
        name: 'Already-Locked Persona',
        subtitle: 'in another room',
        disabled: true,
    },
];

function StatefulPicker({
    initial,
    compact,
}: {
    initial: string[];
    compact?: boolean;
}) {
    const [selected, setSelected] = useState(initial);
    return (
        <PeoplePicker
            people={samplePeople}
            selectedIds={selected}
            onChange={setSelected}
            compact={compact}
        />
    );
}

const meta = {
    title: 'Chat/PeoplePicker',
    component: PeoplePicker,
    parameters: {
        layout: 'centered',
        backgrounds: { default: 'window-body' },
    },
    args: {
        people: samplePeople,
        selectedIds: [],
        onChange: () => {},
    },
    decorators: [
        (Story) => (
            <div style={{ width: 320, padding: 12, background: '#ECE9D8' }}>
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof PeoplePicker>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Empty: Story = {
    args: { people: [] },
};

export const Default: Story = {
    render: (args) => <StatefulPicker initial={args.selectedIds} />,
    args: { selectedIds: ['persona-1'] },
};

export const Compact: Story = {
    render: (args) => (
        <StatefulPicker initial={args.selectedIds} compact={args.compact} />
    ),
    args: { selectedIds: ['persona-2', 'persona-3'], compact: true },
};
