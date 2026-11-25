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
import { Icon } from '../../icons';
import { StartMenu, StartMenuAction, StartMenuItem } from './StartMenu';

const programs = (
    <>
        <StartMenuItem
            icon={<Icon name="world" size={32} />}
            label="World"
            description="Open"
            onClick={fn()}
        />
        <StartMenuItem
            icon={<Icon name="places" size={32} />}
            label="Places"
            description="Open"
            onClick={fn()}
        />
        <StartMenuItem
            icon={<Icon name="messenger" size={32} />}
            label="Messenger"
            description="Open"
            onClick={fn()}
        />
        <StartMenuItem
            icon={<Icon name="board" size={32} />}
            label="Board"
            description="Open"
            onClick={fn()}
        />
    </>
);

const places = (
    <>
        <StartMenuItem
            icon={<Icon name="auto" size={32} />}
            label="Auto Mode"
            onClick={fn()}
        />
        <StartMenuItem
            icon={<Icon name="cookie" size={32} />}
            label="Cookies"
            onClick={fn()}
        />
        <StartMenuItem
            icon={<Icon name="help" size={32} />}
            label="Help and Support"
            onClick={fn()}
        />
        <StartMenuItem
            icon={<Icon name="search" size={32} />}
            label="Search"
            disabled
        />
        <StartMenuItem
            icon={<Icon name="run" size={32} />}
            label="Run..."
            onClick={fn()}
        />
    </>
);

const footer = (
    <>
        <StartMenuAction tone="caution" onClick={fn()}>
            Log Off
        </StartMenuAction>
        <StartMenuAction tone="danger" onClick={fn()}>
            Shut Down
        </StartMenuAction>
    </>
);

const meta = {
    title: 'Chrome/StartMenu',
    component: StartMenu,
    args: {
        user: 'Marigold',
        picture: <Icon name="user" size={32} />,
        programs,
        places,
        footer,
        onClose: fn(),
    },
    decorators: [
        (Story) => (
            <Desk size="large">
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof StartMenu>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const ItemHover: Story = {
    play: preview(
        '.ui-start-menu-programs .ui-start-menu-item:nth-child(2)',
        'hover',
    ),
};

export const ItemFocus: Story = {
    play: preview(
        '.ui-start-menu-places .ui-start-menu-item:first-child',
        'focus',
    ),
};

export const ItemPressed: Story = {
    play: preview(
        '.ui-start-menu-programs .ui-start-menu-item:first-child',
        'active',
    ),
};

export const FooterHover: Story = {
    play: preview('.ui-start-menu-action', 'hover'),
};

export const FooterFocus: Story = {
    play: preview('.ui-start-menu-action[data-tone="danger"]', 'focus'),
};

export const ProgramsOnly: Story = { args: { places: undefined } };

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };

export const OverBusyBackdrop: Story = { ...busy };

export const ReducedTransparency: Story = { ...reducedTransparency };
