import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, within } from 'storybook/test';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { CheckBox } from '../CheckBox/CheckBox';
import { Tabs } from './Tabs';

const page = (text: string) => (
    <div className="ui-story-column">
        <p className="ui-story-p">{text}</p>
        <CheckBox defaultChecked>Apply to every Room</CheckBox>
    </div>
);

const items = [
    {
        value: 'general',
        label: 'General',
        content: page('General settings for this Party.'),
    },
    {
        value: 'appearance',
        label: 'Appearance',
        content: page('How the Party looks on the desktop.'),
    },
    {
        value: 'personas',
        label: 'Personas',
        content: page('Who is invited and who is on auto.'),
    },
    {
        value: 'advanced',
        label: 'Advanced',
        disabled: true,
        content: page('Advanced'),
    },
];

const meta = {
    title: 'Controls/Tabs',
    component: Tabs,
    decorators: [stage],
    args: { items, label: 'Party properties' },
} satisfies Meta<typeof Tabs>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
    render: (args) => <Tabs {...args} className="ui-story-w-360" />,
};

export const AllStates: Story = {
    render: () => (
        <div className="ui-story-column">
            <span className="ui-story-state-label">
                selected (orange hot edge), hover on Personas, focus on General,
                disabled Advanced
            </span>
            <Tabs
                label="States"
                className="ui-story-w-360"
                items={items}
                ref={(node) => {
                    node?.querySelector(
                        '[data-value="personas"]',
                    )?.setAttribute('data-ui-preview', 'hover');
                    node?.querySelector('[data-value="general"]')?.setAttribute(
                        'data-ui-preview',
                        'focus',
                    );
                }}
            />
        </div>
    ),
};

export const Noir: Story = { ...AllStates, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};

export const Keyboard: Story = {
    render: (args) => <Tabs {...args} className="ui-story-w-360" />,
    play: async ({ canvasElement }) => {
        const sheet = within(canvasElement);
        await userEvent.click(sheet.getByRole('tab', { name: 'General' }));
        await userEvent.keyboard('{ArrowRight}');
        const appearance = sheet.getByRole('tab', { name: 'Appearance' });
        await expect(appearance).toHaveFocus();
        await expect(appearance).toHaveAttribute('aria-selected', 'true');
        await expect(sheet.getByRole('tabpanel')).toHaveTextContent(
            'How the Party looks',
        );
        await userEvent.keyboard('{End}');
        await expect(
            sheet.getByRole('tab', { name: 'Personas' }),
        ).toHaveFocus();
        await userEvent.keyboard('{ArrowRight}');
        await expect(sheet.getByRole('tab', { name: 'General' })).toHaveFocus();
    },
};
