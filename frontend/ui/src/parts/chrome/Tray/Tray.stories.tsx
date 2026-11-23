import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import { Icon } from '../../icons';
import {
    noir,
    preview,
    reducedMotion,
    reducedTransparency,
    States,
} from '../story-kit';
import { Tray, TrayButton } from './Tray';

const meta = {
    title: 'Chrome/Tray',
    component: Tray,
    args: {
        clock: '21:04',
        children: (
            <>
                <TrayButton
                    icon={<Icon name="bell" />}
                    label="Notifications"
                    count={3}
                    onClick={fn()}
                />
                <TrayButton
                    icon={<Icon name="moon" />}
                    label="Switch to Noir"
                    onClick={fn()}
                />
                <TrayButton
                    icon={<Icon name="muted" />}
                    label="Sound off"
                    pressed
                    onClick={fn()}
                />
            </>
        ),
    },
    decorators: [
        (Story) => (
            <div className="ui-taskbar">
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof Tray>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const ButtonHover: Story = {
    play: preview('.ui-tray-button:first-child', 'hover'),
};

export const ButtonFocus: Story = {
    play: preview('.ui-tray-button:nth-child(2)', 'focus'),
};

export const ButtonPressed: Story = {
    play: preview('.ui-tray-button:nth-child(2)', 'active'),
};

const bar = (count: number, tone?: 'neutral' | 'go' | 'caution' | 'danger') => (
    <div className="ui-taskbar">
        <Tray clock="9:41">
            <TrayButton
                icon={<Icon name="bell" />}
                label="Notifications"
                count={count}
                countTone={tone}
            />
            <TrayButton icon={<Icon name="sun" />} label="Disabled" disabled />
        </Tray>
    </div>
);

export const Counts: Story = {
    decorators: [(Story) => <Story />],
    render: () => (
        <States
            items={[
                ['no count, disabled', bar(0)],
                ['count', bar(7)],
                ['over 99', bar(140)],
                ['neutral', bar(2, 'neutral')],
                ['go', bar(2, 'go')],
                ['caution', bar(2, 'caution')],
            ]}
        />
    ),
};

export const Noir: Story = { ...noir };

export const ReducedMotion: Story = { ...reducedMotion };

export const ReducedTransparency: Story = { ...reducedTransparency };
