import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../controls/story-kit';
import { Presence, presenceStatuses } from './Presence';
import '../story-kit';

const meta = {
    title: 'Conversation/Presence',
    component: Presence,
    decorators: [stage],
    args: { status: 'online' },
} satisfies Meta<typeof Presence>;

export default meta;

type Story = StoryObj<typeof meta>;

const row = (shape: 'dot' | 'buddy', size: 'small' | 'medium') => (
    <span className="ui-story-chat-row">
        {presenceStatuses.map((status) => (
            <Presence
                key={status}
                status={status}
                shape={shape}
                size={size}
                label={status}
            />
        ))}
    </span>
);

export const Default: Story = {
    render: () => (
        <States
            items={[
                ['dot', row('dot', 'medium')],
                ['dot small', row('dot', 'small')],
                ['buddy', row('buddy', 'medium')],
                ['buddy small', row('buddy', 'small')],
                [
                    'active',
                    <span key="a" className="ui-story-chat-row">
                        <Presence status="online" active label="working" />
                        <Presence status="away" active label="deciding" />
                        <Presence
                            status="online"
                            shape="buddy"
                            active
                            label="typing"
                        />
                    </span>,
                ],
            ]}
        />
    ),
};

export const Noir: Story = { ...Default, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};
