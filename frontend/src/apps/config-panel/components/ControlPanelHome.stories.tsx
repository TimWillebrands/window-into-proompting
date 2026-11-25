import { Icon, Window } from '@partytown/ui';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import ControlPanelHome from './ControlPanelHome';

const meta = {
    title: 'ConfigPanel/ControlPanelHome',
    component: ControlPanelHome,
    parameters: { layout: 'fullscreen' },
    args: { onNavigate: fn() },
    decorators: [
        (Story) => (
            <Window
                title="Control Panel"
                icon={<Icon name="control-panel" />}
                className="h-[480px] w-[720px]"
                bodyClassName="overflow-auto"
            >
                <Story />
            </Window>
        ),
    ],
} satisfies Meta<typeof ControlPanelHome>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
