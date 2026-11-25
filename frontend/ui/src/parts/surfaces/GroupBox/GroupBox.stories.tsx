import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CSSProperties } from 'react';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { CheckBox } from '../../controls/CheckBox/CheckBox';
import { CommandButton } from '../../controls/CommandButton/CommandButton';
import { Icon } from '../../icons';
import { ListView } from '../../views/ListView/ListView';
import { Stack } from '../Stack/Stack';
import { GroupBox } from './GroupBox';

const meta = {
    title: 'Surfaces/GroupBox',
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

const column: CSSProperties = {
    display: 'flex',
    flexDirection: 'column',
    width: 360,
    height: 260,
};

const guests = Array.from({ length: 14 }, (_, index) => ({
    id: `guest-${index}`,
    label: `Guest ${index + 1}`,
    icon: <Icon name="user" />,
}));

export const Fill: Story = {
    render: () => (
        <Stack gap="md" style={column}>
            <GroupBox title="Invite" icon={<Icon name="mail" />}>
                <span>A short box keeps its own height.</span>
            </GroupBox>
            <GroupBox title="Guests" icon={<Icon name="users" />} fill>
                <Stack direction="row">
                    <CommandButton size="small">Add a guest</CommandButton>
                </Stack>
                <ListView label="Guests" view="list" items={guests} />
            </GroupBox>
        </Stack>
    ),
};

export const Noir: Story = { ...Default, ...noir };
export const NoirFill: Story = { ...Fill, ...noir };
export const NoirCurrent: Story = { ...Current, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
