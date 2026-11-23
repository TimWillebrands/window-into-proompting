import type { Meta, StoryObj } from '@storybook/react-vite';
import { Icon } from '../../icons';
import { CheckBox } from '../CheckBox/CheckBox';
import { noir, reducedMotion, reducedTransparency, stage } from '../story-kit';
import { GroupBox } from './GroupBox';

const meta = {
    title: 'Controls/GroupBox',
    component: GroupBox,
    decorators: [stage],
    args: { title: 'Notifications' },
} satisfies Meta<typeof GroupBox>;

export default meta;

type Story = StoryObj<typeof meta>;

const body = (
    <>
        <CheckBox defaultChecked>Show a balloon when someone DMs me</CheckBox>
        <CheckBox>Play a sound for Board replies</CheckBox>
    </>
);

export const Default: Story = {
    render: (args) => (
        <GroupBox {...args} className="ui-story-w-320">
            {body}
        </GroupBox>
    ),
};

export const WithIcon: Story = {
    render: () => (
        <GroupBox
            title="Notifications"
            icon={<Icon name="bell" />}
            className="ui-story-w-320"
        >
            {body}
        </GroupBox>
    ),
};

export const Current: Story = {
    render: () => (
        <div className="ui-story-column">
            <GroupBox
                title="The roof garden"
                icon={<Icon name="tree" />}
                current
                className="ui-story-w-320"
            >
                <span>Your spot · 3 here</span>
            </GroupBox>
            <GroupBox
                title="The bakery"
                icon={<Icon name="cookie" />}
                className="ui-story-w-320"
            >
                <span>Nobody around</span>
            </GroupBox>
        </div>
    ),
};

export const Disabled: Story = {
    render: () => (
        <GroupBox title="Notifications" disabled className="ui-story-w-320">
            {body}
        </GroupBox>
    ),
};

export const Noir: Story = { ...Default, parameters: noir };
export const NoirCurrent: Story = { ...Current, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};
