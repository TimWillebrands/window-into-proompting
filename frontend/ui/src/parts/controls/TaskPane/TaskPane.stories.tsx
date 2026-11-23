import type { Meta, StoryObj } from '@storybook/react-vite';
import { Icon } from '../../icons';
import { noir, reducedMotion, reducedTransparency, stage } from '../story-kit';
import { TaskPane, TaskPaneLink, TaskPaneSection } from './TaskPane';

const meta = {
    title: 'Controls/TaskPane',
    component: TaskPane,
    decorators: [stage],
} satisfies Meta<typeof TaskPane>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: () => (
        <TaskPane aria-label="Party tasks" className="ui-story-taskpane">
            <TaskPaneSection
                title="Party Tasks"
                special
                icon={<Icon name="users" size={32} />}
            >
                <TaskPaneLink icon={<Icon name="add" />}>
                    Invite a Persona
                </TaskPaneLink>
                <TaskPaneLink icon={<Icon name="edit" />}>
                    Rename this Party
                </TaskPaneLink>
                <TaskPaneLink icon={<Icon name="delete" />} disabled>
                    End this Party
                </TaskPaneLink>
            </TaskPaneSection>
            <TaskPaneSection title="Other Places">
                <TaskPaneLink href="#board" icon={<Icon name="board" />}>
                    Board
                </TaskPaneLink>
                <TaskPaneLink href="#diary" icon={<Icon name="diary" />}>
                    Diary
                </TaskPaneLink>
                <TaskPaneLink href="#places" icon={<Icon name="places" />}>
                    Places
                </TaskPaneLink>
            </TaskPaneSection>
            <TaskPaneSection title="Details" defaultOpen={false}>
                <p>
                    <strong>Friday night</strong>
                </p>
                <p>3 Personas, 1 Player</p>
            </TaskPaneSection>
            <TaskPaneSection title="Details (open)">
                <p>
                    <strong>Friday night</strong>
                </p>
                <p>Started 21:04 · World time</p>
            </TaskPaneSection>
        </TaskPane>
    ),
};

export const AllStates: Story = {
    render: () => (
        <TaskPane aria-label="States" className="ui-story-taskpane">
            <TaskPaneSection title="Links">
                <TaskPaneLink icon={<Icon name="add" />}>default</TaskPaneLink>
                <TaskPaneLink
                    icon={<Icon name="add" />}
                    data-ui-preview="hover"
                >
                    hover
                </TaskPaneLink>
                <TaskPaneLink
                    icon={<Icon name="add" />}
                    data-ui-preview="focus"
                >
                    focus
                </TaskPaneLink>
                <TaskPaneLink icon={<Icon name="add" />} disabled>
                    disabled
                </TaskPaneLink>
            </TaskPaneSection>
            <TaskPaneSection title="Not collapsible" collapsible={false}>
                <p>This section always stays open.</p>
            </TaskPaneSection>
        </TaskPane>
    ),
};

export const NarrowWithCounts: Story = {
    render: () => (
        <TaskPane
            aria-label="Control Panel"
            className="ui-story-taskpane-narrow"
        >
            <TaskPaneSection
                title="Control Panel"
                special
                icon={<Icon name="settings" size={32} />}
            >
                <TaskPaneLink icon={<Icon name="back" />}>
                    Switch to Classic View
                </TaskPaneLink>
            </TaskPaneSection>
            <TaskPaneSection title="Places nearby" aside="3 here">
                <TaskPaneLink icon={<Icon name="coffee" />}>
                    The corner café
                </TaskPaneLink>
                <TaskPaneLink icon={<Icon name="tree" />}>
                    The roof garden
                </TaskPaneLink>
            </TaskPaneSection>
            <TaskPaneSection title="Away" aside="12" collapsible={false}>
                <p>Offscreen right now.</p>
            </TaskPaneSection>
        </TaskPane>
    ),
};

export const Noir: Story = { ...Default, parameters: noir };
export const NoirNarrowWithCounts: Story = {
    ...NarrowWithCounts,
    parameters: noir,
};
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};
