import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import {
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
} from '../../../story-kit/story-kit';
import { CommandButton } from '../../controls/CommandButton/CommandButton';
import { Hyperlink } from '../../controls/Hyperlink/Hyperlink';
import { Icon } from '../../icons';
import { LogonAction, LogonScreen, LogonTile } from './LogonScreen';

const meta = {
    title: 'Chrome/LogonScreen',
    component: LogonScreen,
    parameters: { layout: 'fullscreen' },
    args: { title: 'Partytown' },
} satisfies Meta<typeof LogonScreen>;

export default meta;

type Story = StoryObj<typeof meta>;

const footerStart = (
    <LogonAction icon={<Icon name="power" size={32} />} onClick={fn()}>
        Turn off computer
    </LogonAction>
);

const footerEnd =
    'After you sign in, the personas may message you on their own. Your World remembers you.';

const tiles = (selected: string | null) => (
    <>
        <LogonTile
            picture={<Icon name="city" size={48} />}
            name="Partytown"
            subtitle="12 people"
            selected={selected === 'partytown'}
            onClick={fn()}
            footer={
                <Hyperlink onClick={fn()}>
                    Move everyone to a new World…
                </Hyperlink>
            }
        >
            <div className="ui-story-chrome-row">
                <CommandButton isDefault>Sign in</CommandButton>
                <CommandButton>Just look around</CommandButton>
            </div>
        </LogonTile>
        <LogonTile
            picture={<Icon name="city" size={48} />}
            name="Little Harbour"
            subtitle="3 people"
            selected={selected === 'harbour'}
            onClick={fn()}
        />
        <LogonTile
            picture={<Icon name="ruin" size={48} />}
            name="Old Town"
            subtitle="5 people · stopped"
            dimmed
            onClick={fn()}
        />
    </>
);

const screen = (selected: string | null, size?: 'narrow') => (
    <div className="ui-story-logon" data-size={size}>
        <LogonScreen
            title="welcome"
            hint="To begin, click your World"
            footerStart={footerStart}
            footerEnd={footerEnd}
        >
            {tiles(selected)}
        </LogonScreen>
    </div>
);

export const Default: Story = { render: () => screen(null) };

export const Selected: Story = { render: () => screen('partytown') };

export const Hover: Story = {
    render: () => screen(null),
    play: preview('.ui-logon-tile:nth-child(2)', 'hover'),
};

export const Narrow: Story = { render: () => screen('partytown', 'narrow') };

export const Noir: Story = { ...Selected, ...noir };
export const ReducedMotion: Story = { ...Selected, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Selected,
    ...reducedTransparency,
};
