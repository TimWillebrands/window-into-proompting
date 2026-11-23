import type { Meta, StoryObj } from '@storybook/react-vite';
import { ComboBox } from '../../controls/ComboBox/ComboBox';
import { CommandButton } from '../../controls/CommandButton/CommandButton';
import { noir, stage } from '../../controls/story-kit';
import { Text } from '../../controls/Text/Text';
import { Swatch } from '../Swatch/Swatch';
import { ControlGrid, type ControlGridColumn } from './ControlGrid';

const columns: ControlGridColumn[] = [
    {
        key: 'name',
        header: 'Name',
        width: '120px',
        rowHeader: true,
        nowrap: true,
    },
    { key: 'general', header: 'General', title: 'Plans and daily life' },
    { key: 'voice', header: 'Voice' },
    { key: 'reset', header: '', width: '72px', align: 'end' },
];

const pick = (label: string, value: string) => (
    <span className="ui-story-row">
        <Swatch colour={value === 'b' ? '#eb6834' : '#2a78d6'} size="small" />
        <ComboBox
            aria-label={label}
            defaultValue={value}
            options={[
                { value: 'a', label: 'llama3.2' },
                { value: 'b', label: 'nemotron' },
            ]}
        />
    </span>
);

const meta = {
    title: 'Panels/ControlGrid',
    component: ControlGrid,
    decorators: [stage],
    parameters: { stage: { width: 'wide' } },
    args: {
        label: 'Who speaks through which',
        columns,
        rows: [
            {
                id: 'denise',
                cells: {
                    name: 'Denise',
                    general: pick('Denise: General', 'a'),
                    voice: pick('Denise: Voice', 'b'),
                    reset: <CommandButton size="small">Reset</CommandButton>,
                },
            },
            {
                id: 'vlad',
                busy: true,
                cells: {
                    name: 'Vlad',
                    general: pick('Vlad: General', 'b'),
                    voice: pick('Vlad: Voice', 'a'),
                },
            },
            {
                id: 'nobody',
                muted: true,
                title: 'Work that is nobody’s own',
                cells: {
                    name: 'Nobody’s own',
                    general: <Text tone="muted">llama3.2</Text>,
                    voice: <Text tone="muted">General</Text>,
                },
            },
        ],
    },
} satisfies Meta<typeof ControlGrid>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Empty: Story = {
    args: { rows: [], empty: 'No persona matches that.' },
};

export const Narrow: Story = {
    args: { minWidth: '540px', className: 'ui-story-w-320' },
};

export const Noir: Story = {
    parameters: { ...noir, stage: { scheme: 'noir', width: 'wide' } },
};
export const ReducedMotion: Story = {
    parameters: { stage: { reduceMotion: true, width: 'wide' } },
};
export const ReducedTransparency: Story = {
    parameters: { stage: { reduceTransparency: true, width: 'wide' } },
};
