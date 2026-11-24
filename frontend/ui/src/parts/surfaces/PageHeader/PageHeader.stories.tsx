import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { Icon } from '../../icons';
import { WindowLayout } from '../WindowLayout/WindowLayout';
import { PageHeader } from './PageHeader';

const meta = {
    title: 'Surfaces/PageHeader',
    component: PageHeader,
    decorators: [stage],
    args: {
        title: 'Companies',
        description:
            "Real prices, pretend money. Their books never touch anyone's wallet.",
    },
} satisfies Meta<typeof PageHeader>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => (
        <PageHeader
            {...args}
            icon={<Icon name="company" size={32} />}
            className="ui-story-header"
        />
    ),
};

export const WithAside: Story = {
    render: () => (
        <PageHeader
            icon={<Icon name="user" size={32} />}
            title="Ada Lovelace"
            description="barista at the Café · Mon-Fri 09:00-17:00"
            aside="on shift"
            className="ui-story-header"
        />
    ),
};

export const TitleOnly: Story = {
    render: () => (
        <PageHeader
            title="A very long page title that runs out of room and ends in an ellipsis"
            className="ui-story-header"
        />
    ),
};

export const InNarrowWindow: Story = {
    render: (args) => (
        <div style={{ width: 420, height: 120 }}>
            <WindowLayout scroll="none">
                <PageHeader
                    {...args}
                    icon={<Icon name="company" size={32} />}
                />
            </WindowLayout>
        </div>
    ),
};

export const Noir: Story = { ...Default, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
