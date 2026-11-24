import { Icon, Window } from '@partytown/ui';
import type { Meta, StoryObj } from '@storybook/react-vite';
import ModelsSection from './ModelsSection';

const meta = {
    title: 'ConfigPanel/ModelsSection',
    component: ModelsSection,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <Window
                title="Control Panel"
                icon={<Icon name="control-panel" />}
                className="h-[560px] w-[700px]"
                bodyClassName="overflow-auto"
            >
                <Story />
            </Window>
        ),
    ],
} satisfies Meta<typeof ModelsSection>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Adding: Story = { args: { adding: true } };
