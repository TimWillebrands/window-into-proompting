import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../../../story-kit/story-kit';
import { Icon } from '../../icons';
import { CommandButton } from '../CommandButton/CommandButton';
import { Text } from '../Text/Text';
import { StatusText } from './StatusText';

const meta = {
    title: 'Controls/StatusText',
    component: StatusText,
    decorators: [stage],
    args: { tone: 'danger', children: "Couldn't save that change. Try again." },
    argTypes: {
        tone: {
            control: 'inline-radio',
            options: ['neutral', 'go', 'caution', 'danger'],
        },
    },
} satisfies Meta<typeof StatusText>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => <StatusText {...args} role="alert" />,
};

const all = (
    <States
        items={[
            ['neutral', <StatusText key="n">Runs once a night.</StatusText>],
            [
                'go',
                <StatusText key="g" tone="go">
                    Saved.
                </StatusText>,
            ],
            [
                'caution',
                <StatusText key="c" tone="caution">
                    Benched until 21:40
                </StatusText>,
            ],
            [
                'danger',
                <StatusText key="d" tone="danger">
                    No model handles this, so nobody can speak.
                </StatusText>,
            ],
            [
                'custom icon',
                <StatusText key="i" icon={<Icon name="hourglass" />}>
                    Sleeping on it…
                </StatusText>,
            ],
            [
                'no icon',
                <StatusText key="x" tone="go" icon={false}>
                    Ready
                </StatusText>,
            ],
            [
                'inline',
                <span key="s">
                    Provider{' '}
                    <StatusText as="span" tone="caution">
                        Benched
                    </StatusText>
                </span>,
            ],
        ]}
    />
);

export const AllStates: Story = { render: () => all };

const bars = (
    <div className="ui-story-chat-column ui-story-chat-w-420">
        <StatusText variant="bar" tone="caution" icon={<Icon name="scales" />}>
            <Text strong>As Grace:</Text> answer Ada's question.
        </StatusText>
        <StatusText
            variant="bar"
            icon={false}
            actions={
                <CommandButton size="small" onClick={fn()}>
                    Jump to latest
                </CommandButton>
            }
        >
            3 new messages
        </StatusText>
        <StatusText variant="bar" icon={<Icon name="watching" />}>
            You're reading a private conversation. They can't see you.
        </StatusText>
        <StatusText variant="bar" tone="caution">
            No AI participants, so nobody will answer.
        </StatusText>
        <StatusText variant="bar" tone="danger" role="alert">
            Could not save the cast.
        </StatusText>
        <StatusText variant="bar" tone="go">
            Saved.
        </StatusText>
    </div>
);

export const Bar: Story = { render: () => bars };
export const NoirBar: Story = { render: () => bars, ...noir };

export const Noir: Story = { render: () => all, ...noir };
export const ReducedMotion: Story = {
    render: () => all,
    ...reducedMotion,
};
export const ReducedTransparency: Story = {
    render: () => all,
    ...reducedTransparency,
};
