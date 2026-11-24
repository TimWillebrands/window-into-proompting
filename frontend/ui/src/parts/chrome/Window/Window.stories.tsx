import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import {
    busy,
    Desk,
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
    States,
} from '../../../story-kit/story-kit';
import { Icon } from '../../icons';
import { StatusBar, StatusBarField } from '../../surfaces/StatusBar/StatusBar';
import { WindowLayout } from '../../surfaces/WindowLayout/WindowLayout';
import { MenuBar } from '../MenuBar/MenuBar';
import { Window } from './Window';

const menus = [
    {
        label: 'File',
        items: [
            { label: 'New', shortcut: 'Ctrl+N' },
            { label: 'Open…', shortcut: 'Ctrl+O' },
            { type: 'separator' as const },
            { label: 'Exit' },
        ],
    },
    { label: 'Edit', items: [{ label: 'Undo', disabled: true }] },
    { label: 'View', items: [{ label: 'Status Bar', checked: true }] },
    { label: 'Help', items: [{ label: 'About Notepad' }] },
];

const body = (
    <div className="ui-story-body">
        The body is the XP face: opaque, crisp, and the place where content
        lives. The frame around it is solid Luna with a glass finish.
    </div>
);

const meta = {
    title: 'Chrome/Window',
    component: Window,
    args: {
        title: 'Untitled - Notepad',
        icon: <Icon name="notepad" />,
        onMinimise: fn(),
        onMaximise: fn(),
        onClose: fn(),
        children: body,
        className: 'ui-story-window',
    },
    decorators: [
        (Story) => (
            <Desk>
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof Window>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Inactive: Story = { args: { active: false } };

export const WithMenuAndStatusBar: Story = {
    args: {
        children: (
            <WindowLayout
                menuBar={<MenuBar menus={menus} />}
                statusBar={
                    <StatusBar>
                        <StatusBarField grow>Ln 1, Col 1</StatusBarField>
                    </StatusBar>
                }
            >
                {body}
            </WindowLayout>
        ),
    },
};

export const WithStatusBarFields: Story = {
    args: {
        children: (
            <WindowLayout
                statusBar={
                    <StatusBar grip>
                        <StatusBarField grow>
                            After you sign in, the personas may message you.
                        </StatusBarField>
                        <StatusBarField>3 online</StatusBarField>
                    </StatusBar>
                }
            >
                {body}
            </WindowLayout>
        ),
    },
};

export const Maximised: Story = {
    args: { maximised: true },
    decorators: [
        (Story) => (
            <div className="ui-story-window" data-size="wide">
                <Story />
            </div>
        ),
    ],
};

export const TitleBarAccessory: Story = {
    args: {
        title: 'Chat Rooms',
        icon: <Icon name="chat-rooms" />,
        titleBarAccessory: <span>#general · 4 here</span>,
    },
};

export const CloseOnly: Story = {
    args: {
        title: 'Properties',
        onMinimise: undefined,
        onMaximise: undefined,
    },
};

const captions = (active: boolean) => (
    <Window
        title="Caption buttons"
        icon={<Icon name="settings" />}
        active={active}
        className="ui-story-window"
        data-size="small"
        onMinimise={fn()}
        onMaximise={fn()}
        onClose={fn()}
    >
        {body}
    </Window>
);

const closeUp = (active: boolean) => (
    <div
        style={{
            zoom: 3,
            display: 'flex',
            justifyContent: 'flex-end',
            width: 96,
            height: 40,
            overflow: 'hidden',
        }}
    >
        <Window
            title="Caption buttons"
            active={active}
            style={{ flex: 'none', width: 220, height: 80, animation: 'none' }}
            onMinimise={fn()}
            onMaximise={fn()}
            onClose={fn()}
        >
            {body}
        </Window>
    </div>
);

export const CaptionHover: Story = {
    render: () => (
        <States
            layout="tiles"
            items={[
                ['hover, active window', captions(true)],
                ['hover, inactive window', captions(false)],
            ]}
        />
    ),
    play: preview('.ui-window-caption', 'hover'),
};

export const CaptionPressed: Story = {
    render: () => captions(true),
    play: preview('.ui-window-caption', 'active'),
};

export const CaptionFocus: Story = {
    render: () => captions(true),
    play: preview('.ui-window-caption[data-caption="close"]', 'focus'),
};

export const CaptionsCloseUp: Story = {
    render: () => (
        <States
            layout="tiles"
            items={[
                ['active, 3x', closeUp(true)],
                ['inactive, 3x', closeUp(false)],
            ]}
        />
    ),
};

export const Skinned: Story = {
    args: {
        skin: 'msn',
        title: 'Messenger',
        icon: <Icon name="messenger" size={32} />,
    },
};

export const Noir: Story = { ...noir };

export const NoirInactive: Story = { ...noir, args: { active: false } };

export const ReducedMotion: Story = { ...reducedMotion };

export const OverBusyBackdrop: Story = { ...busy };

export const ReducedTransparency: Story = { ...reducedTransparency };
