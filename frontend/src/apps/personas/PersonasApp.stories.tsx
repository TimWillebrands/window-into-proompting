import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import WindowFrame from '../../components/desktop/WindowFrame';
import PersonasApp from './PersonasApp';

const meta = {
    title: 'Apps/PersonasApp',
    component: PersonasApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 760,
                    height: 520,
                    padding: 24,
                    background: '#5C87C2',
                }}
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="personas"
                    title="Persona Manager"
                    icon="👤"
                    width={720}
                    height={480}
                    zIndex={1}
                    onMinimize={fn()}
                    onMaximize={fn()}
                    onClose={fn()}
                >
                    <Story />
                </WindowFrame>
            </div>
        ),
    ],
} satisfies Meta<typeof PersonasApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
