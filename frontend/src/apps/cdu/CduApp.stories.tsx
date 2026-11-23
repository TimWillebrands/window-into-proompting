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
            <div className="h-[600px] w-[880px] p-4">
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="cdu"
                    title="C:\CDU\"
                    icon="cdu"
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
