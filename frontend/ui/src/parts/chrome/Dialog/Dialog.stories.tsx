import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { fn } from 'storybook/test';
import { CommandButton } from '../../controls';
import { Icon } from '../../icons';
import {
    Desk,
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
} from '../story-kit';
import { Dialog } from './Dialog';

const meta = {
    title: 'Chrome/Dialog',
    component: Dialog,
    args: {
        title: 'Leave the party?',
        icon: <Icon name="warning" />,
        onClose: fn(),
        contained: true,
        children: (
            <div className="ui-story-chrome-row">
                <Icon name="warning" size={32} />
                <span>Everyone here will see that you left.</span>
            </div>
        ),
        buttons: (
            <>
                <CommandButton tone="caution" isDefault>
                    Leave
                </CommandButton>
                <CommandButton>Cancel</CommandButton>
            </>
        ),
    },
    decorators: [
        (Story) => (
            <Desk>
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof Dialog>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Destructive: Story = {
    args: {
        title: 'Delete Room',
        icon: <Icon name="delete" />,
        children: <span>Delete “Friday night” and everything said in it?</span>,
        buttons: (
            <>
                <CommandButton tone="danger">Delete</CommandButton>
                <CommandButton isDefault>Cancel</CommandButton>
            </>
        ),
    },
};

export const DisabledAction: Story = {
    args: {
        title: 'Create Room',
        icon: <Icon name="chat-rooms" />,
        children: <span>Give the room a name first.</span>,
        buttons: (
            <>
                <CommandButton tone="go" disabled>
                    Create
                </CommandButton>
                <CommandButton>Cancel</CommandButton>
            </>
        ),
    },
};

export const CloseFocus: Story = {
    play: preview('.ui-window-caption[data-caption="close"]', 'focus'),
};

function Opener() {
    const [open, setOpen] = useState(false);
    return (
        <>
            <CommandButton onClick={() => setOpen(true)}>
                Open the dialog
            </CommandButton>
            {open && (
                <Dialog
                    title="Party Settings"
                    icon={<Icon name="settings" />}
                    onClose={() => setOpen(false)}
                    buttons={
                        <>
                            <CommandButton
                                isDefault
                                onClick={() => setOpen(false)}
                            >
                                OK
                            </CommandButton>
                            <CommandButton onClick={() => setOpen(false)}>
                                Cancel
                            </CommandButton>
                        </>
                    }
                >
                    Tab stays inside, Escape closes, focus returns to the
                    button. Clicking outside flashes the title bar.
                </Dialog>
            )}
        </>
    );
}

export const FocusTrap: Story = { render: () => <Opener /> };

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };

export const ReducedTransparency: Story = { ...reducedTransparency };
