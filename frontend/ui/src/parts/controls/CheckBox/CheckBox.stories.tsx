import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { CheckBox } from './CheckBox';

const meta = {
    title: 'Controls/CheckBox',
    component: CheckBox,
    decorators: [stage],
    args: { children: 'Let Personas start conversations' },
} satisfies Meta<typeof CheckBox>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <div className="ui-story-column">
            <CheckBox defaultChecked>Let Personas start conversations</CheckBox>
            <CheckBox>Show the World clock in the tray</CheckBox>
            <CheckBox indeterminate>Notify me about some Rooms</CheckBox>
        </div>
    ),
};

export const Playground: Story = {};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                ['unchecked', <CheckBox key="u">Remember me</CheckBox>],
                [
                    'checked',
                    <CheckBox key="c" defaultChecked>
                        Remember me
                    </CheckBox>,
                ],
                [
                    'indeterminate',
                    <CheckBox key="m" indeterminate>
                        Remember me
                    </CheckBox>,
                ],
                [
                    'hover',
                    <CheckBox key="h" data-ui-preview="hover">
                        Remember me
                    </CheckBox>,
                ],
                [
                    'focus',
                    <CheckBox key="f" defaultChecked data-ui-preview="focus">
                        Remember me
                    </CheckBox>,
                ],
                [
                    'active',
                    <CheckBox key="a" data-ui-preview="active">
                        Remember me
                    </CheckBox>,
                ],
                [
                    'invalid',
                    <CheckBox key="i" invalid>
                        I accept the house rules
                    </CheckBox>,
                ],
                [
                    'disabled',
                    <CheckBox key="x" disabled>
                        Remember me
                    </CheckBox>,
                ],
                [
                    'disabled checked',
                    <CheckBox key="y" disabled defaultChecked>
                        Remember me
                    </CheckBox>,
                ],
            ]}
        />
    ),
};

export const Noir: Story = { ...AllStates, ...noir };
export const ReducedMotion: Story = { ...AllStates, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...AllStates,
    ...reducedTransparency,
};
