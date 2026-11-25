import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { Icon } from '../../icons';
import { CommandButton } from './CommandButton';

const meta = {
    title: 'Controls/CommandButton',
    component: CommandButton,
    decorators: [stage],
    args: { children: 'OK' },
    argTypes: {
        tone: {
            control: 'inline-radio',
            options: ['neutral', 'go', 'caution', 'danger'],
        },
        size: {
            control: 'inline-radio',
            options: ['small', 'medium', 'large'],
        },
    },
} satisfies Meta<typeof CommandButton>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => (
        <div className="ui-story-row">
            <CommandButton {...args} isDefault>
                OK
            </CommandButton>
            <CommandButton>Cancel</CommandButton>
            <CommandButton>Apply</CommandButton>
        </div>
    ),
};

export const Playground: Story = {};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                ['default', <CommandButton key="d">Cancel</CommandButton>],
                [
                    'hover',
                    <CommandButton key="h" data-ui-preview="hover">
                        Cancel
                    </CommandButton>,
                ],
                [
                    'focus',
                    <CommandButton key="f" data-ui-preview="focus">
                        Cancel
                    </CommandButton>,
                ],
                [
                    'active',
                    <CommandButton key="a" data-ui-preview="active">
                        Cancel
                    </CommandButton>,
                ],
                [
                    'pressed',
                    <CommandButton key="p" pressed>
                        Bold
                    </CommandButton>,
                ],
                [
                    'isDefault',
                    <CommandButton key="i" isDefault>
                        OK
                    </CommandButton>,
                ],
                [
                    'disabled',
                    <CommandButton key="x" disabled>
                        Apply
                    </CommandButton>,
                ],
            ]}
        />
    ),
};

export const Tones: Story = {
    render: () => (
        <States
            items={(['neutral', 'go', 'caution', 'danger'] as const).map(
                (tone) => [
                    tone,
                    <div key={tone} className="ui-story-row">
                        <CommandButton tone={tone}>Button</CommandButton>
                        <CommandButton tone={tone} data-ui-preview="hover">
                            Hover
                        </CommandButton>
                        <CommandButton tone={tone} data-ui-preview="active">
                            Active
                        </CommandButton>
                        <CommandButton tone={tone} data-ui-preview="focus">
                            Focus
                        </CommandButton>
                        <CommandButton tone={tone} disabled>
                            Disabled
                        </CommandButton>
                    </div>,
                ],
            )}
        />
    ),
};

export const Sizes: Story = {
    render: () => (
        <div className="ui-story-row">
            <CommandButton size="small">Small</CommandButton>
            <CommandButton size="medium">Medium</CommandButton>
            <CommandButton size="large">Large</CommandButton>
        </div>
    ),
};

export const WithIcon: Story = {
    render: () => (
        <div className="ui-story-row">
            <CommandButton tone="go" icon={<Icon name="add" />}>
                New Party
            </CommandButton>
            <CommandButton icon={<Icon name="save" />}>Save</CommandButton>
            <CommandButton tone="danger" icon={<Icon name="delete" />}>
                Delete
            </CommandButton>
        </div>
    ),
};

export const Noir: Story = { ...AllStates, ...noir };
export const NoirTones: Story = { ...Tones, ...noir };
export const ReducedMotion: Story = { ...AllStates, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...AllStates,
    ...reducedTransparency,
};
