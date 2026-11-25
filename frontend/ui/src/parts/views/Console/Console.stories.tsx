import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { Console, ConsoleLine } from './Console';

const meta = {
    title: 'Views/Console',
    component: Console,
    decorators: [stage],
} satisfies Meta<typeof Console>;

export default meta;

type Story = StoryObj<typeof meta>;

const lines = (
    <>
        <ConsoleLine tone="muted">C:\WORK&gt; listen</ConsoleLine>
        <ConsoleLine prompt=">" tone="neutral">
            Ada: can you tidy the README?
        </ConsoleLine>
        <ConsoleLine prompt="...">Reading the README first.</ConsoleLine>
        <ConsoleLine prompt="$" tone="go">
            git diff --stat
        </ConsoleLine>
        <ConsoleLine prompt="--" tone="muted">
            1 file changed, 4 insertions(+), 9 deletions(-)
        </ConsoleLine>
        <ConsoleLine prompt="cdu>">
            Done. Shorter and it says what it is.
        </ConsoleLine>
        <ConsoleLine prompt="!!" tone="danger">
            push refused: the branch moved
        </ConsoleLine>
        <ConsoleLine prompt="~" tone="caution">
            retrying in a moment
        </ConsoleLine>
    </>
);

export const Default: Story = {
    render: () => (
        <Console role="log" aria-label="Output" className="ui-story-console">
            {lines}
        </Console>
    ),
};

export const Tones: Story = {
    render: () => (
        <Console role="log" aria-label="Tones" className="ui-story-console">
            <ConsoleLine>default</ConsoleLine>
            <ConsoleLine tone="muted">muted</ConsoleLine>
            <ConsoleLine tone="neutral">neutral</ConsoleLine>
            <ConsoleLine tone="go">go</ConsoleLine>
            <ConsoleLine tone="caution">caution</ConsoleLine>
            <ConsoleLine tone="danger">danger</ConsoleLine>
            <ConsoleLine prompt="$" tone="go">
                a long line that wraps instead of scrolling sideways, because
                output is read, not measured, and nobody wants a horizontal
                scrollbar in a console
            </ConsoleLine>
        </Console>
    ),
};

export const Noir: Story = { ...Default, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
