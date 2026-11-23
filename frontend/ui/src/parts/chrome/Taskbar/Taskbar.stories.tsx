import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { expect, fn, userEvent, waitFor, within } from 'storybook/test';
import { Icon } from '../../icons';
import { StartButton } from '../StartButton/StartButton';
import {
    busy,
    Desk,
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
} from '../story-kit';
import { Tray, TrayButton } from '../Tray/Tray';
import { Taskbar, TaskbarButton } from './Taskbar';

const windows = [
    { id: 'personas', label: 'Personas', icon: 'personas' },
    { id: 'rooms', label: 'Chat Rooms', icon: 'chat-rooms' },
    { id: 'panel', label: 'Control Panel', icon: 'control-panel' },
    { id: 'notepad', label: 'Untitled - Notepad', icon: 'notepad' },
] as const;

const tray = (
    <Tray clock="21:04">
        <TrayButton
            icon={<Icon name="bell" />}
            label="Notifications"
            count={3}
        />
        <TrayButton icon={<Icon name="moon" />} label="Switch to Noir" />
    </Tray>
);

const meta = {
    title: 'Chrome/Taskbar',
    component: Taskbar,
    parameters: { layout: 'fullscreen' },
    args: {
        start: <StartButton />,
        tray,
        className: 'ui-story-bottom',
        children: windows.map((w, index) => (
            <TaskbarButton
                key={w.id}
                taskId={w.id}
                label={w.label}
                icon={<Icon name={w.icon} />}
                active={index === 2}
                onClick={fn()}
                onMinimise={fn()}
                onMaximise={fn()}
                onClose={fn()}
            />
        )),
    },
    decorators: [
        (Story) => (
            <Desk size="full">
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof Taskbar>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Empty: Story = { args: { children: null } };

export const ButtonHover: Story = {
    play: preview('.ui-taskbar-button:first-child', 'hover'),
};

export const ButtonFocus: Story = {
    play: preview('.ui-taskbar-button:nth-child(2)', 'focus'),
};

export const ButtonPressed: Story = {
    play: preview('.ui-taskbar-button:nth-child(2)', 'active'),
};

export const LongTitles: Story = {
    args: {
        children: [
            'Denise — Conversation with Henkie Spenkie and Yusuf',
            'Untitled - Notepad',
            'Control Panel',
        ].map((label, index) => (
            <TaskbarButton
                key={label}
                taskId={`long-${index}`}
                label={label}
                title={label}
                icon={<Icon name={index === 0 ? 'messenger' : 'notepad'} />}
                active={index === 0}
                onClick={fn()}
            />
        )),
    },
};

function Desktop() {
    const [open, setOpen] = useState<
        Array<{ id: string; label: string; icon: 'personas' | 'chat-rooms' }>
    >([
        { id: 'personas', label: 'Personas', icon: 'personas' },
        { id: 'rooms', label: 'Chat Rooms', icon: 'chat-rooms' },
    ]);
    const [minimised, setMinimised] = useState<string[]>(['personas']);
    const close = (id: string) =>
        setOpen((windows) => windows.filter((w) => w.id !== id));
    return (
        <Taskbar
            start={<StartButton />}
            tray={tray}
            className="ui-story-bottom"
        >
            {open.map((w) => {
                const isMinimised = minimised.includes(w.id);
                return (
                    <TaskbarButton
                        key={w.id}
                        taskId={w.id}
                        label={w.label}
                        icon={<Icon name={w.icon} />}
                        active={!isMinimised}
                        minimised={isMinimised}
                        onRestore={() =>
                            setMinimised((m) => m.filter((x) => x !== w.id))
                        }
                        onMinimise={() => setMinimised((m) => [...m, w.id])}
                        onClose={() => close(w.id)}
                    />
                );
            })}
        </Taskbar>
    );
}

export const WindowMenu: Story = {
    render: () => <Desktop />,
    play: async ({ canvasElement }) => {
        const page = within(canvasElement.ownerDocument.body);
        const rooms = page.getByRole('button', { name: 'Chat Rooms' });
        await userEvent.pointer({ keys: '[MouseRight]', target: rooms });
        const menu = await page.findByRole('menu', { name: 'Chat Rooms' });
        const inMenu = within(menu);
        await expect(
            inMenu.getByRole('menuitem', { name: 'Restore' }),
        ).toHaveAttribute('aria-disabled', 'true');
        await expect(
            inMenu.getByRole('menuitem', { name: 'Maximise' }),
        ).toHaveAttribute('aria-disabled', 'true');
        await expect(
            inMenu.getByRole('menuitem', { name: 'Minimise' }),
        ).not.toHaveAttribute('aria-disabled');
    },
};

export const WindowMenuCloses: Story = {
    render: () => <Desktop />,
    play: async ({ canvasElement }) => {
        const page = within(canvasElement.ownerDocument.body);
        const rooms = page.getByRole('button', { name: 'Chat Rooms' });
        rooms.focus();
        await userEvent.keyboard('{Shift>}{F10}{/Shift}');
        const menu = await page.findByRole('menu', { name: 'Chat Rooms' });
        await userEvent.click(
            within(menu).getByRole('menuitem', { name: 'Close' }),
        );
        await waitFor(() =>
            expect(
                page.queryByRole('button', { name: 'Chat Rooms' }),
            ).not.toBeInTheDocument(),
        );
        const personas = page.getByRole('button', { name: 'Personas' });
        personas.focus();
        await userEvent.keyboard('{ContextMenu}');
        const again = await page.findByRole('menu', { name: 'Personas' });
        await userEvent.click(
            within(again).getByRole('menuitem', { name: 'Restore' }),
        );
        await expect(personas).toHaveAttribute('aria-pressed', 'true');
    },
};

export const Crowded: Story = {
    args: {
        children: Array.from({ length: 12 }, (_, index) => `w${index + 1}`).map(
            (id) => (
                <TaskbarButton
                    key={id}
                    taskId={id}
                    label={`Window ${id}`}
                    icon={<Icon name="document" />}
                    onClick={fn()}
                />
            ),
        ),
    },
};

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };

export const OverBusyBackdrop: Story = { ...busy };

export const ReducedTransparency: Story = { ...reducedTransparency };
