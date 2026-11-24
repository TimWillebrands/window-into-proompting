import type { Meta, StoryObj } from '@storybook/react-vite';
import StoryWindow from '../../components/desktop/StoryWindow';
import CduApp from './CduApp';

const meta = {
    title: 'Apps/CduApp',
    component: CduApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[600px] w-[480px] p-4'
                        : 'h-[600px] w-[880px] p-4'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow id="cdu" title="C:\CDU\" icon="cdu">
                    <Story />
                </StoryWindow>
            </div>
        ),
    ],
} satisfies Meta<typeof CduApp>;

export default meta;

export const Awake: StoryObj<typeof meta> = {};

export const Narrow: StoryObj<typeof meta> = { parameters: { narrow: true } };
