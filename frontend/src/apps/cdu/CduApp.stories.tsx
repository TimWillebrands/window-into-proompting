import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import WindowFrame from '../../components/desktop/WindowFrame';
import CduApp from './CduApp';

const meta = {
    title: 'Apps/CduApp',
    component: CduApp,
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
                    id="cdu"
                    title="C:\CDU\"
                    icon="🜂"
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
} satisfies Meta<typeof CduApp>;

export default meta;

export const Awake: StoryObj<typeof meta> = {};
