import type { Meta, StoryObj } from '@storybook/react-vite';
import { Icon } from '../../icons';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    States,
    stage,
} from '../story-kit';
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

export const Noir: Story = { render: () => all, parameters: noir };
export const ReducedMotion: Story = {
    render: () => all,
    parameters: reducedMotion,
};
export const ReducedTransparency: Story = {
    render: () => all,
    parameters: reducedTransparency,
};
