import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../story-kit';
import { RadioButton, RadioGroup } from './RadioButton';

const meta = {
    title: 'Controls/RadioButton',
    component: RadioButton,
    decorators: [stage],
    args: { value: 'a', children: 'Option' },
} satisfies Meta<typeof RadioButton>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <RadioGroup name="pace" defaultValue="normal" aria-label="World pace">
            <RadioButton value="slow">Slow: a day takes an hour</RadioButton>
            <RadioButton value="normal">Normal</RadioButton>
            <RadioButton value="fast">Fast</RadioButton>
            <RadioButton value="frozen" disabled>
                Frozen (stopped Worlds only)
            </RadioButton>
        </RadioGroup>
    ),
};

export const Horizontal: Story = {
    render: () => (
        <RadioGroup
            name="view"
            defaultValue="tiles"
            orientation="horizontal"
            aria-label="View"
        >
            <RadioButton value="tiles">Tiles</RadioButton>
            <RadioButton value="icons">Icons</RadioButton>
            <RadioButton value="details">Details</RadioButton>
        </RadioGroup>
    ),
};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                [
                    'unchecked',
                    <RadioButton key="u" name="s1" value="a">
                        Option
                    </RadioButton>,
                ],
                [
                    'checked',
                    <RadioButton key="c" name="s2" value="a" defaultChecked>
                        Option
                    </RadioButton>,
                ],
                [
                    'hover',
                    <RadioButton
                        key="h"
                        name="s3"
                        value="a"
                        data-ui-preview="hover"
                    >
                        Option
                    </RadioButton>,
                ],
                [
                    'focus',
                    <RadioButton
                        key="f"
                        name="s4"
                        value="a"
                        defaultChecked
                        data-ui-preview="focus"
                    >
                        Option
                    </RadioButton>,
                ],
                [
                    'active',
                    <RadioButton
                        key="a"
                        name="s5"
                        value="a"
                        data-ui-preview="active"
                    >
                        Option
                    </RadioButton>,
                ],
                [
                    'invalid',
                    <RadioButton key="i" name="s6" value="a" invalid>
                        Option
                    </RadioButton>,
                ],
                [
                    'disabled',
                    <RadioButton key="x" name="s7" value="a" disabled>
                        Option
                    </RadioButton>,
                ],
                [
                    'disabled checked',
                    <RadioButton
                        key="y"
                        name="s8"
                        value="a"
                        disabled
                        defaultChecked
                    >
                        Option
                    </RadioButton>,
                ],
            ]}
        />
    ),
};

export const Noir: Story = { ...AllStates, parameters: noir };
export const ReducedMotion: Story = { ...AllStates, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...AllStates,
    parameters: reducedTransparency,
};
