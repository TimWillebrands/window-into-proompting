import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent } from 'storybook/test';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../controls/story-kit';
import { Text } from '../../controls/Text/Text';
import { Expander } from './Expander';
import '../story-kit';

const meta = {
    title: 'Conversation/Expander',
    component: Expander,
    decorators: [stage],
    args: { title: 'Online', count: 3 },
} satisfies Meta<typeof Expander>;

export default meta;

type Story = StoryObj<typeof meta>;

const body = (
    <>
        <Text>Ada</Text>
        <Text>Grace</Text>
        <Text>Linus</Text>
    </>
);

export const Default: Story = {
    render: (args) => (
        <div className="ui-story-chat-column ui-story-chat-w-260">
            <Expander {...args}>{body}</Expander>
            <Expander title="Busy" count={0} defaultOpen={false}>
                {body}
            </Expander>
        </div>
    ),
};

export const Toggles: Story = {
    render: (args) => (
        <div className="ui-story-chat-w-260">
            <Expander {...args}>{body}</Expander>
        </div>
    ),
    play: async ({ canvas }) => {
        const toggle = canvas.getByRole('button', { name: /online/i });
        await expect(toggle).toHaveAttribute('aria-expanded', 'true');
        await userEvent.click(toggle);
        await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    },
};

export const Noir: Story = { ...Default, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};
