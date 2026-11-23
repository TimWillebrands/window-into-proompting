import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn, userEvent } from 'storybook/test';
import { Icon } from '../../icons';
import {
    busy,
    Desk,
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
} from '../story-kit';
import { ContextMenu, useContextMenu } from './ContextMenu';
import type { MenuEntry } from './menu';

const items: MenuEntry[] = [
    { label: 'Open', icon: <Icon name="folder-open" />, onSelect: fn() },
    { label: 'Rename', icon: <Icon name="edit" />, shortcut: 'F2' },
    {
        label: 'Send To',
        icon: <Icon name="mail" />,
        items: [
            { label: 'Board', icon: <Icon name="board" /> },
            { label: 'Diary', icon: <Icon name="diary" /> },
            {
                label: 'A Persona',
                icon: <Icon name="personas" />,
                items: [{ label: 'Marigold' }, { label: 'Basil' }],
            },
        ],
    },
    { type: 'separator' },
    { label: 'Show Hidden', checked: true },
    { label: 'Paste', shortcut: 'Ctrl+V', disabled: true },
    { type: 'separator' },
    {
        label: 'Delete',
        icon: <Icon name="delete" />,
        tone: 'danger',
        shortcut: 'Del',
    },
    { label: 'Properties', icon: <Icon name="settings" /> },
];

const meta = {
    title: 'Chrome/ContextMenu',
    component: ContextMenu,
    args: {
        open: true,
        at: { x: 160, y: 120 },
        items,
        onClose: fn(),
        label: 'File actions',
    },
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <Desk size="full">
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof ContextMenu>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const ItemHover: Story = {
    play: preview('.ui-menu-item:first-child', 'hover'),
};

export const ItemFocus: Story = {
    play: preview('.ui-menu-item:nth-child(2)', 'focus'),
};

export const SubmenuOpen: Story = {
    play: async ({ canvasElement }) => {
        const item = canvasElement.ownerDocument.querySelector(
            '.ui-menu-item[aria-haspopup]',
        );
        if (item) await userEvent.hover(item);
    },
};

function RightClick() {
    const menu = useContextMenu();
    return (
        <>
            <button
                type="button"
                className="ui-story-target"
                onContextMenu={menu.onContextMenu}
            >
                Right-click here (or focus and press Shift+F10)
            </button>
            <ContextMenu {...menu.menuProps} items={items} label="Actions" />
        </>
    );
}

export const RightClickTarget: Story = { render: () => <RightClick /> };

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };

export const OverBusyBackdrop: Story = { ...busy };

export const ReducedTransparency: Story = { ...reducedTransparency };
