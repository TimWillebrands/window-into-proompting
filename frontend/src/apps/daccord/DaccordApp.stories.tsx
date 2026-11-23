import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    createMemoryHistory,
    createRootRoute,
    createRouter,
    RouterContextProvider,
} from '@tanstack/react-router';
import { fn } from 'storybook/test';
import WindowFrame from '../../components/desktop/WindowFrame';
import { WindowInstanceProvider } from '../../components/desktop/window-instance-context';
import DaccordApp from './DaccordApp';

const rootRoute = createRootRoute();
const stubRouter = createRouter({
    routeTree: rootRoute,
    history: createMemoryHistory({ initialEntries: ['/'] }),
});

const meta = {
    title: 'Apps/DaccordApp',
    component: DaccordApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div className="h-[580px] w-[880px] p-6">
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="open-party"
                    title="D'Accord — Chat Rooms"
                    icon="/img/icons/chat-rooms.ico"
                    width={840}
                    height={540}
                    zIndex={1}
                    onMinimize={fn()}
                    onMaximize={fn()}
                    onClose={fn()}
                >
                    <RouterContextProvider router={stubRouter}>
                        <WindowInstanceProvider windowId="open-party">
                            <Story />
                        </WindowInstanceProvider>
                    </RouterContextProvider>
                </WindowFrame>
            </div>
        ),
    ],
} satisfies Meta<typeof DaccordApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
