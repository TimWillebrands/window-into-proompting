import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../story-kit';
import { Hyperlink } from './Hyperlink';

const meta = {
    title: 'Controls/Hyperlink',
    component: Hyperlink,
    decorators: [stage],
    args: { children: 'Polder Capital' },
} satisfies Meta<typeof Hyperlink>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <p className="ui-story-p">
            Works at <Hyperlink>Polder Capital</Hyperlink>, see the{' '}
            <Hyperlink href="#pr">open pull request</Hyperlink>.
        </p>
    ),
};

export const AllStates: Story = {
    render: () => (
        <States
            items={[
                ['default', <Hyperlink key="d">Show older posts</Hyperlink>],
                [
                    'hover',
                    <Hyperlink key="h" data-ui-preview="hover">
                        Show older posts
                    </Hyperlink>,
                ],
                [
                    'focus',
                    <Hyperlink key="f" data-ui-preview="focus">
                        Show older posts
                    </Hyperlink>,
                ],
                [
                    'disabled',
                    <Hyperlink key="x" disabled>
                        Show older posts
                    </Hyperlink>,
                ],
                [
                    'anchor',
                    <Hyperlink key="a" href="#somewhere">
                        An anchor
                    </Hyperlink>,
                ],
            ]}
        />
    ),
};

export const Noir: Story = { ...AllStates, parameters: noir };
export const ReducedMotion: Story = {
    ...AllStates,
    parameters: reducedMotion,
};
export const ReducedTransparency: Story = {
    ...AllStates,
    parameters: reducedTransparency,
};
