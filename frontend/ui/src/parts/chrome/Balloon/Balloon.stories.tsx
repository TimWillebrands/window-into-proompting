import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { fn } from 'storybook/test';
import { CommandButton } from '../../controls';
import { Icon } from '../../icons';
import {
    busy,
    Desk,
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
    States,
} from '../story-kit';
import { Balloon } from './Balloon';

const meta = {
    title: 'Chrome/Balloon',
    component: Balloon,
    args: {
        title: 'Cookies',
        icon: <Icon name="cookie" />,
        onClose: fn(),
        children: (
            <div className="ui-story-chrome-row">
                <span>
                    Cookies to see how Partytown gets used, incl. recordings?
                </span>
                <CommandButton size="small" tone="go">
                    Allow
                </CommandButton>
                <CommandButton size="small">No thanks</CommandButton>
            </div>
        ),
    },
    decorators: [
        (Story) => (
            <Desk>
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof Balloon>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Placements: Story = {
    render: (args) => (
        <States
            items={[
                [
                    'above, tail end',
                    <Balloon key="ae" {...args} placement="above" />,
                ],
                [
                    'above, tail start',
                    <Balloon
                        key="as"
                        {...args}
                        placement="above"
                        tailAlign="start"
                    />,
                ],
                [
                    'below, tail centre',
                    <Balloon
                        key="bc"
                        {...args}
                        placement="below"
                        tailAlign="center"
                    />,
                ],
            ]}
        />
    ),
};

export const TextOnly: Story = {
    args: {
        title: undefined,
        icon: undefined,
        onClose: undefined,
        children: 'Your Persona is back from the café.',
    },
};

export const CloseHover: Story = {
    play: preview('.ui-balloon-close', 'hover'),
};

function Anchored() {
    const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null);
    const [open, setOpen] = useState(true);
    return (
        <>
            <CommandButton ref={setAnchor} onClick={() => setOpen(!open)}>
                Anchor
            </CommandButton>
            {open && anchor && (
                <Balloon
                    anchor={anchor}
                    title="New message"
                    icon={<Icon name="messenger" />}
                    onClose={() => setOpen(false)}
                >
                    Basil wrote to you.
                </Balloon>
            )}
        </>
    );
}

export const AnchoredToElement: Story = { render: () => <Anchored /> };

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };

export const OverBusyBackdrop: Story = { ...busy };

export const ReducedTransparency: Story = { ...reducedTransparency };
