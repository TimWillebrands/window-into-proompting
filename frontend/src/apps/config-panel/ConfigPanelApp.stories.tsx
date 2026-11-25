import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    getGetLlmConfigAssignmentsQueryKey,
    getGetLlmConfigPersonaRoutesQueryKey,
    getGetLlmConfigProvidersAvailabilityQueryKey,
    getGetLlmConfigProvidersQueryKey,
    getGetPersonaQueryKey,
} from '#api/party-zone';
import { seeded } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { useNavHistoryStore } from '../../lib/nav-history';
import ConfigPanelApp from './ConfigPanelApp';
import {
    assignmentsFor,
    personas,
    providers,
    routes,
} from './components/modelFixtures';

const at = (section?: string) => () => {
    useNavHistoryStore.setState({
        stack: [
            { kind: 'window', windowId: 'config-panel' },
            ...(section ? [{ kind: 'config' as const, section }] : []),
        ],
        index: section ? 1 : 0,
        isNavigating: false,
    });
};

const meta = {
    title: 'Apps/ConfigPanelApp',
    component: ConfigPanelApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[560px] w-[480px] p-6'
                        : 'h-[560px] w-[800px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow
                    id="config-panel"
                    title="Control Panel"
                    icon="control-panel"
                >
                    <Story />
                </StoryWindow>
            </div>
        ),
    ],
} satisfies Meta<typeof ConfigPanelApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = { beforeEach: at() };

export const LanguageModels: Story = {
    beforeEach: at('llm-providers'),
    decorators: [
        seeded([
            [getGetLlmConfigProvidersQueryKey(), providers],
            [getGetLlmConfigPersonaRoutesQueryKey(), routes],
            [getGetLlmConfigProvidersAvailabilityQueryKey(), []],
            [getGetLlmConfigAssignmentsQueryKey(), assignmentsFor()],
            [getGetPersonaQueryKey(), personas],
        ]),
    ],
};

export const Narrow: Story = {
    ...LanguageModels,
    parameters: { narrow: true },
};
