import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import WindowFrame from '../../components/desktop/WindowFrame';
import WorksApp from './WorksApp';

const meta = {
    title: 'Apps/WorksApp',
    component: WorksApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 880,
                    height: 600,
                    padding: 24,
                    background: '#5C87C2',
                }}
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="works"
                    title="C:\WORKS\cmd.exe"
                    icon="🏗️"
                    width={840}
                    height={560}
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
} satisfies Meta<typeof WorksApp>;

export default meta;

export const Working: StoryObj<typeof meta> = {};
