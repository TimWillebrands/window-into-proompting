import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { expect, userEvent, within } from 'storybook/test';
import { Icon } from '../../icons';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../story-kit';
import { Toolbar, ToolbarButton, ToolbarSeparator } from './Toolbar';

const meta = {
    title: 'Controls/Toolbar',
    component: Toolbar,
    decorators: [stage],
} satisfies Meta<typeof Toolbar>;

export default meta;

type Story = StoryObj<typeof meta>;

function Explorer({ labels }: { labels?: 'right' | 'below' | 'none' }) {
    const [bold, setBold] = useState(true);
    return (
        <Toolbar aria-label="Standard Buttons" labels={labels}>
            <ToolbarButton icon={<Icon name="back" />} hasMenu>
                Back
            </ToolbarButton>
            <ToolbarButton icon={<Icon name="forward" />} disabled>
                Forward
            </ToolbarButton>
            <ToolbarButton icon={<Icon name="up" />} aria-label="Up" />
            <ToolbarSeparator />
            <ToolbarButton icon={<Icon name="search" />}>Search</ToolbarButton>
            <ToolbarButton
                icon={<Icon name="folder" />}
                pressed={bold}
                onClick={() => setBold(!bold)}
            >
                Folders
            </ToolbarButton>
            <ToolbarSeparator />
            <ToolbarButton
                icon={<Icon name="settings" />}
                aria-label="Views"
                hasMenu
            />
        </Toolbar>
    );
}

export const Default: Story = {
    render: () => <Explorer />,
};

export const LabelsBelow: Story = {
    render: () => <Explorer labels="below" />,
};

export const IconsOnly: Story = {
    render: () => <Explorer labels="none" />,
};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                [
                    'default',
                    <ToolbarButton key="d" icon={<Icon name="search" />}>
                        Search
                    </ToolbarButton>,
                ],
                [
                    'hover',
                    <ToolbarButton
                        key="h"
                        icon={<Icon name="search" />}
                        data-ui-preview="hover"
                    >
                        Search
                    </ToolbarButton>,
                ],
                [
                    'focus',
                    <ToolbarButton
                        key="f"
                        icon={<Icon name="search" />}
                        data-ui-preview="focus"
                    >
                        Search
                    </ToolbarButton>,
                ],
                [
                    'active',
                    <ToolbarButton
                        key="a"
                        icon={<Icon name="search" />}
                        data-ui-preview="active"
                    >
                        Search
                    </ToolbarButton>,
                ],
                [
                    'pressed',
                    <ToolbarButton
                        key="p"
                        icon={<Icon name="folder" />}
                        pressed
                    >
                        Folders
                    </ToolbarButton>,
                ],
                [
                    'disabled',
                    <ToolbarButton
                        key="x"
                        icon={<Icon name="forward" />}
                        disabled
                    >
                        Forward
                    </ToolbarButton>,
                ],
            ]}
        />
    ),
};

export const Vertical: Story = {
    render: () => (
        <Toolbar aria-label="Tools" orientation="vertical" labels="none">
            <ToolbarButton icon={<Icon name="edit" />}>Edit</ToolbarButton>
            <ToolbarButton icon={<Icon name="scissors" />} pressed>
                Cut
            </ToolbarButton>
            <ToolbarSeparator />
            <ToolbarButton icon={<Icon name="delete" />}>Delete</ToolbarButton>
        </Toolbar>
    ),
};

export const Bare: Story = {
    render: () => (
        <div className="ui-story-column">
            <div className="ui-story-surface">
                <Toolbar aria-label="Regular">
                    <ToolbarButton icon={<Icon name="undo" />}>
                        Undo
                    </ToolbarButton>
                    <ToolbarButton icon={<Icon name="redo" />}>
                        Redo
                    </ToolbarButton>
                </Toolbar>
            </div>
            <div className="ui-story-surface">
                <Toolbar aria-label="Bare" variant="bare">
                    <ToolbarButton icon={<Icon name="undo" />}>
                        Undo
                    </ToolbarButton>
                    <ToolbarButton icon={<Icon name="redo" />}>
                        Redo
                    </ToolbarButton>
                </Toolbar>
            </div>
        </div>
    ),
};

export const Noir: Story = { ...AllStates, parameters: noir };
export const NoirToolbar: Story = { ...Default, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};

export const Keyboard: Story = {
    render: () => <Explorer />,
    play: async ({ canvasElement }) => {
        const bar = within(canvasElement);
        const back = bar.getByRole('button', { name: /Back/ });
        await expect(back).toHaveAttribute('tabindex', '0');
        await userEvent.tab();
        await expect(back).toHaveFocus();
        await userEvent.keyboard('{ArrowRight}');
        await expect(bar.getByRole('button', { name: 'Up' })).toHaveFocus();
        await userEvent.keyboard('{ArrowRight}{ArrowRight}');
        const folders = bar.getByRole('button', { name: /Folders/ });
        await expect(folders).toHaveFocus();
        await userEvent.keyboard(' ');
        await expect(folders).toHaveAttribute('aria-pressed', 'false');
        await userEvent.keyboard('{End}');
        await expect(bar.getByRole('button', { name: 'Views' })).toHaveFocus();
    },
};
