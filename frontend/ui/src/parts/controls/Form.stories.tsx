import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../story-kit/story-kit';
import { GroupBox } from '../surfaces/GroupBox/GroupBox';
import { CheckBox } from './CheckBox/CheckBox';
import { ComboBox } from './ComboBox/ComboBox';
import { CommandButton } from './CommandButton/CommandButton';
import { FieldRow } from './FieldRow/FieldRow';
import { RadioButton, RadioGroup } from './RadioButton/RadioButton';
import { Slider } from './Slider/Slider';
import { Tabs } from './Tabs/Tabs';
import { TextBox } from './TextBox/TextBox';

const meta = {
    title: 'Controls/Form',
    decorators: [stage],
} satisfies Meta;

export default meta;

type Story = StoryObj<typeof meta>;

function General() {
    return (
        <div className="ui-story-column">
            <GroupBox title="Party">
                <FieldRow label="Name:" required>
                    <TextBox defaultValue="Friday night" />
                </FieldRow>
                <FieldRow label="Host model:">
                    <ComboBox
                        defaultValue="sonnet"
                        options={[
                            { value: 'sonnet', label: 'Claude Sonnet' },
                            { value: 'opus', label: 'Claude Opus' },
                            { value: 'haiku', label: 'Claude Haiku' },
                        ]}
                    />
                </FieldRow>
                <FieldRow label="Talkativeness:">
                    <Slider
                        defaultValue={6}
                        min={0}
                        max={10}
                        ticks={11}
                        startLabel="Quiet"
                        endLabel="Chatty"
                    />
                </FieldRow>
            </GroupBox>
            <GroupBox title="World pace">
                <RadioGroup
                    name="pace"
                    defaultValue="normal"
                    aria-label="World pace"
                >
                    <RadioButton value="slow">Slow</RadioButton>
                    <RadioButton value="normal">Normal</RadioButton>
                    <RadioButton value="fast">Fast</RadioButton>
                </RadioGroup>
            </GroupBox>
            <CheckBox defaultChecked>Let Personas start conversations</CheckBox>
        </div>
    );
}

export const Default: Story = {
    render: () => (
        <form
            className="ui-story-dialog-body"
            onSubmit={(event) => event.preventDefault()}
        >
            <Tabs
                label="Party properties"
                items={[
                    {
                        value: 'general',
                        label: 'General',
                        content: <General />,
                    },
                    {
                        value: 'guests',
                        label: 'Guests',
                        content: <p>Guests</p>,
                    },
                    {
                        value: 'advanced',
                        label: 'Advanced',
                        content: <p>Advanced</p>,
                    },
                ]}
            />
            <div className="ui-story-buttons">
                <CommandButton isDefault>OK</CommandButton>
                <CommandButton>Cancel</CommandButton>
                <CommandButton disabled>Apply</CommandButton>
            </div>
        </form>
    ),
};

export const Noir: Story = { ...Default, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
