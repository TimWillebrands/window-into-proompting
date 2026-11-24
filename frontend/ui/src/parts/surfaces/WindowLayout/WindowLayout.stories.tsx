import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CSSProperties, ReactNode } from 'react';
import { fn } from 'storybook/test';
import {
    Desk,
    noir,
    reducedMotion,
    reducedTransparency,
} from '../../../story-kit/story-kit';
import { MenuBar } from '../../chrome/MenuBar/MenuBar';
import { Window } from '../../chrome/Window/Window';
import { ComboBox } from '../../controls/ComboBox/ComboBox';
import { CommandButton } from '../../controls/CommandButton/CommandButton';
import { FieldRow } from '../../controls/FieldRow/FieldRow';
import { Text } from '../../controls/Text/Text';
import { TextArea } from '../../controls/TextArea/TextArea';
import {
    Toolbar,
    ToolbarButton,
    ToolbarSeparator,
} from '../../controls/Toolbar/Toolbar';
import { Icon, type IconName } from '../../icons';
import {
    CategoryItem,
    CategoryView,
} from '../../views/CategoryView/CategoryView';
import { ChatLog } from '../../views/ChatLog/ChatLog';
import { ChatMessage } from '../../views/ChatMessage/ChatMessage';
import { ListView } from '../../views/ListView/ListView';
import { PropertyList } from '../../views/PropertyList/PropertyList';
import { Stack } from '../Stack/Stack';
import { StatusBar, StatusBarField } from '../StatusBar/StatusBar';
import { TaskPane, TaskPaneLink, TaskPaneSection } from '../TaskPane/TaskPane';
import { WindowLayout, type WindowLayoutProps } from './WindowLayout';

const menus = (app: string) => [
    {
        label: 'File',
        items: [
            { label: 'New', shortcut: 'Ctrl+N' },
            { type: 'separator' as const },
            { label: 'Close' },
        ],
    },
    { label: 'Edit', items: [{ label: 'Select All', shortcut: 'Ctrl+A' }] },
    { label: 'View', items: [{ label: 'Status Bar', checked: true }] },
    { label: 'Favorites', items: [{ label: 'Add to Favorites…' }] },
    { label: 'Tools', items: [{ label: 'Folder Options…' }] },
    { label: 'Help', items: [{ label: `About ${app}` }] },
];

const files: ReadonlyArray<{
    id: string;
    name: string;
    icon: IconName;
    kind: string;
    size: string;
}> = [
    { id: 'board', name: 'Board', icon: 'board', kind: 'App', size: '12 KB' },
    { id: 'diary', name: 'Diary', icon: 'diary', kind: 'App', size: '48 KB' },
    {
        id: 'messenger',
        name: 'Messenger',
        icon: 'messenger',
        kind: 'App',
        size: '7 KB',
    },
    {
        id: 'places',
        name: 'Places',
        icon: 'places',
        kind: 'App',
        size: '230 KB',
    },
    {
        id: 'personas',
        name: 'Personas',
        icon: 'folder',
        kind: 'File Folder',
        size: '',
    },
    { id: 'jobs', name: 'Jobs', icon: 'folder', kind: 'File Folder', size: '' },
    {
        id: 'notes',
        name: 'Notes for the party.txt',
        icon: 'notepad',
        kind: 'Text Document',
        size: '2 KB',
    },
    {
        id: 'photo',
        name: 'Rooftop at dusk.jpg',
        icon: 'picture',
        kind: 'JPEG Image',
        size: '311 KB',
    },
    {
        id: 'music',
        name: 'Saturday mix.mp3',
        icon: 'music',
        kind: 'MP3 Audio',
        size: '4,210 KB',
    },
    {
        id: 'calc',
        name: 'Budget.xls',
        icon: 'calculator',
        kind: 'Spreadsheet',
        size: '19 KB',
    },
];

const listItems = files.map((file) => ({
    id: file.id,
    label: file.name,
    icon: <Icon name={file.icon} />,
    cells: { kind: file.kind, size: file.size },
}));

const columns = [
    { key: 'name', header: 'Name' },
    { key: 'size', header: 'Size', align: 'end' as const },
    { key: 'kind', header: 'Type' },
];

function Frame({
    title,
    icon,
    width = 760,
    height = 480,
    children,
}: {
    title: string;
    icon: IconName;
    width?: number;
    height?: number;
    children: ReactNode;
}) {
    const size: CSSProperties = { width, height };
    return (
        <Window
            title={title}
            icon={<Icon name={icon} />}
            onMinimise={fn()}
            onMaximise={fn()}
            onClose={fn()}
            style={size}
        >
            {children}
        </Window>
    );
}

const navigation = (
    <Toolbar aria-label="Standard Buttons">
        <ToolbarButton icon={<Icon name="back" />}>Back</ToolbarButton>
        <ToolbarButton icon={<Icon name="forward" />} disabled>
            Forward
        </ToolbarButton>
        <ToolbarButton icon={<Icon name="up" />} aria-label="Up" />
        <ToolbarSeparator />
        <ToolbarButton icon={<Icon name="search" />}>Search</ToolbarButton>
        <ToolbarButton icon={<Icon name="folder-open" />}>
            Folders
        </ToolbarButton>
    </Toolbar>
);

const address = (place: string) => (
    <Stack direction="row" gap="xs" align="center">
        <Stack.Item grow>
            <FieldRow label="Address" labelWidth="auto">
                <ComboBox
                    value={place}
                    onChange={fn()}
                    options={[{ value: place, label: place }]}
                />
            </FieldRow>
        </Stack.Item>
        <CommandButton size="small" icon={<Icon name="forward" />}>
            Go
        </CommandButton>
    </Stack>
);

const fileTasks = (
    <TaskPane>
        <TaskPaneSection title="File and Folder Tasks">
            <TaskPaneLink icon={<Icon name="new" />}>
                Make a new folder
            </TaskPaneLink>
            <TaskPaneLink icon={<Icon name="internet" />}>
                Publish this folder to the Web
            </TaskPaneLink>
            <TaskPaneLink icon={<Icon name="link" />}>
                Share this folder
            </TaskPaneLink>
        </TaskPaneSection>
        <TaskPaneSection title="Other Places">
            <TaskPaneLink icon={<Icon name="computer" />}>
                My Computer
            </TaskPaneLink>
            <TaskPaneLink icon={<Icon name="folder" />}>
                Shared Documents
            </TaskPaneLink>
        </TaskPaneSection>
        <TaskPaneSection title="Details">
            <Text as="p" strong>
                My Party
            </Text>
            <Text as="p">File Folder</Text>
        </TaskPaneSection>
    </TaskPane>
);

const explorerStatus = (
    <StatusBar grip>
        <StatusBarField grow>{files.length} objects</StatusBarField>
        <StatusBarField icon={<Icon name="computer" />}>
            My Computer
        </StatusBarField>
    </StatusBar>
);

function Explorer({
    details,
    detailsPlacement,
    ...layout
}: {
    details?: boolean;
    detailsPlacement?: 'end' | 'bottom';
} & Pick<
    WindowLayoutProps,
    | 'taskPaneWidth'
    | 'taskPaneNarrow'
    | 'narrowBelow'
    | 'detailsWidth'
    | 'detailsNarrow'
    | 'detailsNarrowBelow'
>) {
    return (
        <WindowLayout
            menuBar={<MenuBar menus={menus('Windows')} />}
            toolbar={navigation}
            addressBar={address('C:\\My Party')}
            taskPane={fileTasks}
            taskPaneLabel="File and folder tasks"
            detailsPane={
                details ? (
                    <Stack pad="lg" gap="md">
                        <Icon name="notepad" size={48} />
                        <Text as="h3" size="title">
                            Notes for the party.txt
                        </Text>
                        <PropertyList
                            layout="stacked"
                            items={[
                                { label: 'Type', value: 'Text Document' },
                                { label: 'Size', value: '2 KB' },
                                { label: 'Modified', value: 'Today, 21:04' },
                            ]}
                        />
                    </Stack>
                ) : undefined
            }
            detailsPlacement={detailsPlacement}
            statusBar={explorerStatus}
            {...layout}
            scroll="none"
        >
            <ListView
                label="My Party"
                view="details"
                items={listItems}
                columns={columns}
                defaultSelected={details ? ['notes'] : undefined}
            />
        </WindowLayout>
    );
}

const meta = {
    title: 'Surfaces/WindowLayout',
    component: WindowLayout,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <Desk size="full">
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof WindowLayout>;

export default meta;

type Story = StoryObj<typeof meta>;

export const ExplorerLike: Story = {
    render: () => (
        <Frame title="My Party" icon="folder-open">
            <Explorer />
        </Frame>
    ),
};

const categories: ReadonlyArray<{
    id: string;
    icon: IconName;
    title: string;
    description: string;
}> = [
    {
        id: 'appearance',
        icon: 'palette',
        title: 'Appearance and Themes',
        description: 'Change the scheme, the desktop background or fonts.',
    },
    {
        id: 'models',
        icon: 'models',
        title: 'Language Models',
        description: 'Add a provider and choose who speaks with which model.',
    },
    {
        id: 'accounts',
        icon: 'users',
        title: 'User Accounts',
        description: 'Change your picture, name or password.',
    },
    {
        id: 'sounds',
        icon: 'music',
        title: 'Sounds and Audio Devices',
        description: 'Change the sound scheme or the volume.',
    },
    {
        id: 'network',
        icon: 'internet',
        title: 'Network Connections',
        description: 'Connect to the Internet and to a tailnet.',
    },
    {
        id: 'security',
        icon: 'shield',
        title: 'Security Center',
        description: 'Check who can see what you share.',
    },
];

function ControlPanel() {
    return (
        <WindowLayout
            menuBar={<MenuBar menus={menus('Control Panel')} />}
            toolbar={navigation}
            addressBar={address('Control Panel')}
            taskPane={
                <TaskPane>
                    <TaskPaneSection
                        title="Control Panel"
                        icon={<Icon name="control-panel" size={32} />}
                        special
                        collapsible={false}
                    >
                        <TaskPaneLink icon={<Icon name="refresh" />}>
                            Switch to Classic View
                        </TaskPaneLink>
                    </TaskPaneSection>
                    <TaskPaneSection title="See Also">
                        <TaskPaneLink icon={<Icon name="help" />}>
                            Help and Support
                        </TaskPaneLink>
                        <TaskPaneLink icon={<Icon name="internet" />}>
                            Windows Update
                        </TaskPaneLink>
                    </TaskPaneSection>
                </TaskPane>
            }
            taskPaneLabel="Control Panel tasks"
            contentLabel="Pick a category"
        >
            <CategoryView title="Pick a category">
                {categories.map((category) => (
                    <CategoryItem
                        key={category.id}
                        icon={<Icon name={category.icon} size={48} />}
                        title={category.title}
                        description={category.description}
                    />
                ))}
            </CategoryView>
        </WindowLayout>
    );
}

export const ControlPanelLike: Story = {
    render: () => (
        <Frame title="Control Panel" icon="control-panel">
            <ControlPanel />
        </Frame>
    ),
};

const lines = [
    ['Ada', 'morning! did anyone see the rooftop last night?'],
    ['Grace', 'I did, the lanterns were lovely'],
    ['Ada', 'we should do it again on Saturday'],
    ['Grace', 'only if someone brings the good mugs'],
    ['Ada', 'obviously'],
    ['Grace', 'I will ask around who else is coming'],
    ['Ada', 'bring the mix from last time too'],
    ['Grace', 'the one with the accordion?'],
    ['Ada', 'that one'],
] as const;

function Chat() {
    return (
        <WindowLayout
            toolbar={
                <Toolbar aria-label="Conversation">
                    <ToolbarButton icon={<Icon name="users" />}>
                        Invite
                    </ToolbarButton>
                    <ToolbarButton icon={<Icon name="document" />}>
                        Send Files
                    </ToolbarButton>
                    <ToolbarButton icon={<Icon name="emoticon" />}>
                        Activities
                    </ToolbarButton>
                </Toolbar>
            }
            scroll="none"
            footer={
                <Stack direction="row" gap="sm" align="end">
                    <Stack grow>
                        <TextArea
                            rows={2}
                            aria-label="Message"
                            placeholder="Say something"
                        />
                    </Stack>
                    <CommandButton isDefault>Send</CommandButton>
                </Stack>
            }
            statusBar={
                <StatusBar>
                    <StatusBarField grow>
                        Last message received at 21:04.
                    </StatusBarField>
                </StatusBar>
            }
        >
            <Stack pad="sm" grow>
                <ChatLog aria-label="Conversation with Grace">
                    {lines.map(([sender, text], index) => (
                        <ChatMessage
                            // biome-ignore lint/suspicious/noArrayIndexKey: static story lines
                            key={index}
                            layout="transcript"
                            sender={`${sender} says:`}
                            own={sender === 'Grace'}
                        >
                            {text}
                        </ChatMessage>
                    ))}
                </ChatLog>
            </Stack>
        </WindowLayout>
    );
}

export const ChatLike: Story = {
    render: () => (
        <Frame title="Grace - Conversation" icon="messenger" width={460}>
            <Chat />
        </Frame>
    ),
};

export const DetailsPane: Story = {
    render: () => (
        <Frame title="My Party" icon="folder-open" width={900}>
            <Explorer details />
        </Frame>
    ),
};

export const DetailsPaneBottom: Story = {
    render: () => (
        <Frame title="My Party" icon="folder-open" height={560}>
            <Explorer details detailsPlacement="bottom" />
        </Frame>
    ),
};

export const DetailsWide: Story = {
    render: () => (
        <Frame title="My Party" icon="folder-open" width={900}>
            <Explorer details detailsWidth="wide" />
        </Frame>
    ),
};

export const CustomTaskPaneWidth: Story = {
    render: () => (
        <Frame title="My Party" icon="folder-open" width={900}>
            <Explorer taskPaneWidth="300px" narrowBelow={640} />
        </Frame>
    ),
};

export const BreakpointHidesTaskPane: Story = {
    render: () => (
        <Frame title="My Party" icon="folder-open" width={760}>
            <Explorer taskPaneNarrow="hide" narrowBelow={820} />
        </Frame>
    ),
};

export const BreakpointHidesDetails: Story = {
    render: () => (
        <Frame title="My Party" icon="folder-open" width={900}>
            <Explorer details detailsNarrow="hide" detailsNarrowBelow={1000} />
        </Frame>
    ),
};

export const Narrow: Story = {
    render: () => (
        <Frame title="My Party" icon="folder-open" width={420} height={560}>
            <Explorer />
        </Frame>
    ),
};

export const NarrowHidesTaskPane: Story = {
    render: () => (
        <Frame title="Control Panel" icon="control-panel" width={420}>
            <WindowLayout
                toolbar={navigation}
                taskPane={fileTasks}
                taskPaneNarrow="hide"
                contentLabel="Pick a category"
            >
                <CategoryView title="Pick a category">
                    {categories.map((category) => (
                        <CategoryItem
                            key={category.id}
                            icon={<Icon name={category.icon} size={48} />}
                            title={category.title}
                            description={category.description}
                        />
                    ))}
                </CategoryView>
            </WindowLayout>
        </Frame>
    ),
};

const longRead = Array.from(
    { length: 14 },
    (_, index) =>
        `Entry ${index + 1}. Walked to the market; the baker remembered my name. Rain all day after that, so I stayed in with a book about lighthouses.`,
);

export const ScrollContent: Story = {
    render: () => (
        <Frame title="Diary" icon="diary" width={520} height={360}>
            <WindowLayout
                toolbar={navigation}
                contentLabel="Diary"
                statusBar={
                    <StatusBar>
                        <StatusBarField grow>
                            Only the page scrolls; the bars stay put.
                        </StatusBarField>
                    </StatusBar>
                }
            >
                <Stack pad="lg" gap="md">
                    {longRead.map((entry) => (
                        <Text key={entry} as="p" size="reading">
                            {entry}
                        </Text>
                    ))}
                </Stack>
            </WindowLayout>
        </Frame>
    ),
};

export const ScrollBoth: Story = {
    render: () => (
        <Frame title="Seating plan" icon="chair" width={420} height={300}>
            <WindowLayout scroll="both" contentLabel="Seating plan">
                <Stack direction="row" gap="md" pad="lg">
                    {categories.map((category) => (
                        <Stack key={category.id} gap="xs" align="center">
                            <Icon name={category.icon} size={48} />
                            <Text>{category.title}</Text>
                        </Stack>
                    ))}
                </Stack>
            </WindowLayout>
        </Frame>
    ),
};

export const Noir: Story = { ...ExplorerLike, ...noir };
export const NoirControlPanel: Story = { ...ControlPanelLike, ...noir };
export const NoirChat: Story = { ...ChatLike, ...noir };
export const ReducedMotion: Story = { ...ExplorerLike, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...ExplorerLike,
    ...reducedTransparency,
};
