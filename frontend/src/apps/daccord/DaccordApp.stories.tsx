import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    createMemoryHistory,
    createRootRoute,
    createRouter,
    RouterContextProvider,
} from '@tanstack/react-router';
import {
    getGetPartyIdChatGroupsChatGroupIdDriverOverridesQueryKey,
    getGetPartyIdChatGroupsChatGroupIdParticipantsQueryKey,
} from '#api/party-zone';
import { useDesktopStore } from '#lib/desktop-context';
import { seeded } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WindowInstanceProvider } from '../../components/desktop/window-instance-context';
import { WORLD_ID } from '../../lib/world';
import DaccordApp from './DaccordApp';

const rootRoute = createRootRoute();
const stubRouter = createRouter({
    routeTree: rootRoute,
    history: createMemoryHistory({ initialEntries: ['/'] }),
});

const SIZES = {
    regular: { width: 840, height: 540 },
    preset: { width: 720, height: 480 },
    narrow: { width: 460, height: 560 },
    wide: { width: 1100, height: 600 },
} as const;

const inRoom = () => {
    const state = {
        desktopState: {
            windows: { 'open-party': { props: { roomId: 'cg-1' } } },
            order: [],
            focusedId: null,
            zCounter: 0,
            startMenuOpen: false,
        },
        navigate: null,
    };
    (
        useDesktopStore as unknown as {
            mockImplementation: (
                f: (s?: (v: unknown) => unknown) => unknown,
            ) => void;
        }
    ).mockImplementation((selector) => (selector ? selector(state) : state));
};

const meta = {
    title: 'Apps/DaccordApp',
    component: DaccordApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story, { parameters }) => {
            const size =
                SIZES[(parameters.size as keyof typeof SIZES) ?? 'regular'];
            return (
                <div
                    className="p-6"
                    style={{ width: size.width + 40, height: size.height + 40 }}
                >
                    {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                    <StoryWindow
                        id="open-party"
                        title="D'Accord — Chat Rooms"
                        icon="/img/icons/chat-rooms.ico"
                    >
                        <RouterContextProvider router={stubRouter}>
                            <WindowInstanceProvider windowId="open-party">
                                <Story />
                            </WindowInstanceProvider>
                        </RouterContextProvider>
                    </StoryWindow>
                </div>
            );
        },
        seeded([
            [
                getGetPartyIdChatGroupsChatGroupIdParticipantsQueryKey(
                    WORLD_ID,
                    'cg-1',
                ),
                ['persona-1', 'persona-2', 'persona-3'],
            ],
            [
                getGetPartyIdChatGroupsChatGroupIdDriverOverridesQueryKey(
                    WORLD_ID,
                    'cg-1',
                ),
                [],
            ],
        ]),
    ],
} satisfies Meta<typeof DaccordApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Narrow: Story = { parameters: { size: 'narrow' } };

export const Wide: Story = { parameters: { size: 'wide' } };

export const Room: Story = { beforeEach: inRoom };

export const RoomPresetSize: Story = {
    beforeEach: inRoom,
    parameters: { size: 'preset' },
};

export const RoomNarrow: Story = {
    beforeEach: inRoom,
    parameters: { size: 'narrow' },
};
