import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import { Icon } from '../../icons';
import type { MenuEntry } from '../ContextMenu/menu';
import { Desk, noir, preview, reducedMotion } from '../story-kit';
import { Window } from '../Window/Window';
import { MenuBar } from './MenuBar';

const file: MenuEntry[] = [
    { label: 'New Room', icon: <Icon name="new" />, shortcut: 'Ctrl+N' },
    { label: 'Open…', icon: <Icon name="folder-open" />, shortcut: 'Ctrl+O' },
    { label: 'Save', icon: <Icon name="save" />, shortcut: 'Ctrl+S' },
    { type: 'separator' },
    {
        label: 'Recent',
        items: [{ label: 'Friday night' }, { label: 'Book club' }],
    },
    { type: 'separator' },
    { label: 'Exit', onSelect: fn() },
];

const menus = [
    { label: 'File', items: file },
    {
        label: 'Edit',
        items: [
            { label: 'Undo', shortcut: 'Ctrl+Z', disabled: true },
            { label: 'Cut', shortcut: 'Ctrl+X' },
            { label: 'Copy', shortcut: 'Ctrl+C' },
        ],
    },
    {
        label: 'View',
        items: [
            { label: 'Status Bar', checked: true },
            { label: 'Word Wrap', checked: false },
        ],
    },
    { label: 'Favorites', items: [], disabled: true },
    { label: 'Help', items: [{ label: 'About' }] },
];

const meta = {
    title: 'Chrome/MenuBar',
    component: MenuBar,
    args: { menus },
    decorators: [
        (Story) => (
            <Desk>
                <Window
                    title="Untitled - Notepad"
                    icon={<Icon name="notepad" />}
                    className="ui-story-window"
                    menuBar={<Story />}
                    onClose={fn()}
                >
                    <div className="ui-story-body">
                        Click a menu, or focus it and use the arrow keys.
                    </div>
                </Window>
            </Desk>
        ),
    ],
} satisfies Meta<typeof MenuBar>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Hover: Story = {
    play: preview('.ui-menubar-item:first-child', 'hover'),
};

export const Focus: Story = {
    play: preview('.ui-menubar-item:nth-child(2)', 'focus'),
};

export const Open: Story = {
    play: async ({ canvasElement }) => {
        canvasElement.ownerDocument
            .querySelector<HTMLButtonElement>('.ui-menubar-item')
            ?.click();
    },
};

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };
