import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import {
    busy,
    Desk,
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
} from '../../../story-kit/story-kit';
import {
    CommandButton,
    ProgressBar,
    Toolbar,
    ToolbarButton,
} from '../../controls';
import { Icon } from '../../icons';
import { HudPanel } from './HudPanel';

const meta = {
    title: 'Chrome/HudPanel',
    component: HudPanel,
    args: {
        anchor: 'top-start',
        title: 'The Corner Café · 3 here',
        icon: <Icon name="coffee" />,
    },
    globals: { backgrounds: { value: 'busy' } },
    decorators: [
        (Story) => (
            <Desk>
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof HudPanel>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithBody: Story = {
    args: {
        anchor: 'top',
        title: 'Walking to the Library',
        icon: <Icon name="walk" />,
        children: (
            <>
                <ProgressBar value={40} label="On the way" />
                <span>1:12 left</span>
            </>
        ),
    },
};

export const Closable: Story = {
    args: {
        anchor: 'top-start',
        title: 'Ana (human)',
        icon: <Icon name="standing" size={32} />,
        onClose: fn(),
        children: (
            <>
                <span>Reading by the window.</span>
                <CommandButton size="small">Change look</CommandButton>
            </>
        ),
    },
};

export const CloseHover: Story = {
    ...Closable,
    play: preview('.ui-hud-panel-close', 'hover'),
};

export const WithToolbar: Story = {
    args: {
        anchor: 'top-end',
        title: undefined,
        icon: undefined,
        children: (
            <Toolbar orientation="vertical" labels="none" aria-label="Zoom">
                <ToolbarButton
                    aria-label="Zoom in"
                    icon={<Icon name="zoom-in" />}
                />
                <ToolbarButton
                    aria-label="Zoom out"
                    icon={<Icon name="zoom-out" />}
                />
                <ToolbarButton
                    aria-label="Back to you"
                    icon={<Icon name="locate" />}
                />
            </Toolbar>
        ),
    },
};

export const Anchors: Story = {
    render: () => (
        <>
            {(
                [
                    'top-start',
                    'top',
                    'top-end',
                    'bottom-start',
                    'bottom',
                    'bottom-end',
                ] as const
            ).map((anchor) => (
                <HudPanel key={anchor} anchor={anchor}>
                    {anchor}
                </HudPanel>
            ))}
        </>
    ),
};

export const TopKeepsClearOfCorners: Story = {
    render: () => (
        <div style={{ position: 'relative', width: 460, height: 160 }}>
            <HudPanel
                anchor="top"
                icon={<Icon name="walk" />}
                title="The Corner Café → The Library on the far side of the park"
            >
                <ProgressBar value={40} label="On the way" valueText="1:12" />
            </HudPanel>
            <HudPanel anchor="top-end">
                <Toolbar orientation="vertical" aria-label="Zoom">
                    <ToolbarButton
                        aria-label="Zoom in"
                        icon={<Icon name="zoom-in" />}
                    />
                    <ToolbarButton
                        aria-label="Zoom out"
                        icon={<Icon name="zoom-out" />}
                    />
                </Toolbar>
            </HudPanel>
        </div>
    ),
};

export const InFlow: Story = {
    render: () => (
        <div className="ui-story-column">
            <HudPanel title="Stacked by the app" icon={<Icon name="info" />}>
                Without an anchor a panel sits in the app's layout.
            </HudPanel>
            <HudPanel>The app stacks several in a corner.</HudPanel>
        </div>
    ),
};

export const Noir: Story = { ...Closable, ...noir };

export const ReducedMotion: Story = { ...Closable, ...reducedMotion };

export const OverBusyBackdrop: Story = { ...Closable, ...busy };

export const ReducedTransparency: Story = {
    ...Closable,
    ...reducedTransparency,
};
