import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { ComboBox } from '../ComboBox/ComboBox';
import { Slider } from '../Slider/Slider';
import { TextArea } from '../TextArea/TextArea';
import { TextBox } from '../TextBox/TextBox';
import { FieldRow } from './FieldRow';

const meta = {
    title: 'Controls/FieldRow',
    component: FieldRow,
    decorators: [stage],
    args: { label: 'Name:', children: null },
} satisfies Meta<typeof FieldRow>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <div className="ui-story-column ui-story-w-360">
            <FieldRow label="Party name:" required>
                <TextBox defaultValue="Friday night" />
            </FieldRow>
            <FieldRow
                label="Model:"
                hint="Used when a Persona has none of its own."
            >
                <ComboBox
                    defaultValue="sonnet"
                    options={[
                        { value: 'sonnet', label: 'Claude Sonnet' },
                        { value: 'haiku', label: 'Claude Haiku' },
                    ]}
                />
            </FieldRow>
            <FieldRow label="Talkativeness:">
                <Slider
                    defaultValue={40}
                    startLabel="Quiet"
                    endLabel="Chatty"
                />
            </FieldRow>
        </div>
    ),
};

export const Stacked: Story = {
    render: () => (
        <div className="ui-story-column ui-story-w-360">
            <FieldRow label="Welcome message:" layout="stacked">
                <TextArea
                    rows={3}
                    defaultValue="Come on in, the kettle is on."
                />
            </FieldRow>
        </div>
    ),
};

const speakers = [
    { value: 'me', label: 'Myself' },
    { value: 'marigold', label: 'Marigold' },
];

export const LabelWidths: Story = {
    render: () => (
        <div className="ui-story-column ui-story-w-360">
            <FieldRow label="Send as:" labelWidth="auto">
                <ComboBox defaultValue="me" options={speakers} />
            </FieldRow>
            <FieldRow label="Hue:" labelWidth="compact">
                <Slider defaultValue={30} startLabel="Cool" endLabel="Warm" />
            </FieldRow>
            <FieldRow label="Default model for new Parties:" labelWidth="14em">
                <TextBox defaultValue="Claude Sonnet" />
            </FieldRow>
        </div>
    ),
};

export const Invalid: Story = {
    render: () => (
        <div className="ui-story-column ui-story-w-360">
            <FieldRow
                label="Party name:"
                required
                error="A Party needs a name."
            >
                <TextBox defaultValue="" />
            </FieldRow>
            <FieldRow label="Disabled:">
                <TextBox defaultValue="party-7f3a" disabled />
            </FieldRow>
        </div>
    ),
};

export const Noir: Story = { ...Invalid, ...noir };
export const NoirLabelWidths: Story = { ...LabelWidths, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
