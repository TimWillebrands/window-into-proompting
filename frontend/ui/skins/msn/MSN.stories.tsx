import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CSSProperties, ReactNode } from 'react';
import { fn } from 'storybook/test';
import { Window } from '../../src/parts/chrome/Window/Window';
import { CommandButton } from '../../src/parts/controls/CommandButton/CommandButton';
import { Presence } from '../../src/parts/controls/Presence/Presence';
import { Text } from '../../src/parts/controls/Text/Text';
import { TextArea } from '../../src/parts/controls/TextArea/TextArea';
import { TextBox } from '../../src/parts/controls/TextBox/TextBox';
import {
    Toolbar,
    ToolbarButton,
} from '../../src/parts/controls/Toolbar/Toolbar';
import { UserPicture } from '../../src/parts/controls/UserPicture/UserPicture';
import { Icon } from '../../src/parts/icons';
import { Expander } from '../../src/parts/surfaces/Expander/Expander';
import { Pane } from '../../src/parts/surfaces/Pane/Pane';
import { Stack } from '../../src/parts/surfaces/Stack/Stack';
import {
    StatusBar,
    StatusBarField,
} from '../../src/parts/surfaces/StatusBar/StatusBar';
import { WindowLayout } from '../../src/parts/surfaces/WindowLayout/WindowLayout';
import { ChatLog } from '../../src/parts/views/ChatLog/ChatLog';
import { ChatMessage } from '../../src/parts/views/ChatMessage/ChatMessage';
import { ContactRow } from '../../src/parts/views/ContactRow/ContactRow';
import type { Scheme } from '../../src/scheme';
import {
    Desk,
    noir,
    picture,
    preview,
    reducedMotion,
    reducedTransparency,
    States,
} from '../../src/story-kit/story-kit';

const contactSize: CSSProperties = { width: 300, height: 560 };
const compactSize: CSSProperties = { width: 280, height: 360 };
const chatSize: CSSProperties = { width: 540, height: 440 };
const grow: CSSProperties = { flex: '1 1 0%', minHeight: 0 };

type Buddy = {
    name: string;
    status: 'online' | 'away' | 'busy' | 'offline';
    note?: string;
};

const buddies: Buddy[] = [
    { name: 'Ada', status: 'online', note: 'reading by the window' },
    { name: 'Linus', status: 'online' },
    { name: 'Margaret', status: 'away', note: 'back in five' },
    { name: 'Alan', status: 'busy', note: 'in a meeting' },
    { name: 'Grace', status: 'offline' },
];

const statusLabel = {
    online: 'Online',
    away: 'Away',
    busy: 'Busy',
    offline: 'Offline',
};

const row = (buddy: Buddy) => (
    <ContactRow
        key={buddy.name}
        picture={<Presence shape="buddy" status={buddy.status} />}
        dimmed={buddy.status === 'offline'}
        name={
            <>
                {buddy.name}
                {buddy.status !== 'online' && (
                    <Text tone="muted"> ({statusLabel[buddy.status]})</Text>
                )}
                {buddy.note && (
                    <Text tone="muted" italic>
                        {' '}
                        - {buddy.note}
                    </Text>
                )}
            </>
        }
        onClick={fn()}
    />
);

function ContactList({
    active = true,
    compact = false,
}: {
    active?: boolean;
    compact?: boolean;
}) {
    const online = buddies.filter((buddy) => buddy.status !== 'offline');
    const offline = buddies.filter((buddy) => buddy.status === 'offline');
    return (
        <Window
            skin="msn"
            title="Messenger"
            icon={<Icon name="messenger" size={32} />}
            active={active}
            style={compact ? compactSize : contactSize}
            onMinimise={fn()}
            onMaximise={fn()}
            onClose={fn()}
        >
            <WindowLayout
                scroll="none"
                contentLabel="Contacts"
                toolbar={[
                    <Stack
                        key="me"
                        direction="row"
                        gap="md"
                        pad="md"
                        align="center"
                    >
                        <UserPicture src={picture(28)} size={48} />
                        <Stack.Item grow>
                            <Stack gap="xxs">
                                <Stack direction="row" gap="xs" align="center">
                                    <Presence shape="buddy" status="online" />
                                    <Stack.Item truncate>
                                        <Text strong>Grace</Text>
                                    </Stack.Item>
                                    <Text tone="muted">(Online)</Text>
                                </Stack>
                                <Text tone="muted" italic>
                                    &lt;Type a personal message&gt;
                                </Text>
                            </Stack>
                        </Stack.Item>
                    </Stack>,
                    <Stack key="find" pad="sm">
                        <TextBox
                            type="search"
                            placeholder="Find a contact..."
                            aria-label="Find a contact"
                        />
                    </Stack>,
                ]}
                statusBar={
                    <StatusBar>
                        <StatusBarField>
                            {buddies.length} contacts
                        </StatusBarField>
                        <StatusBarField grow>
                            Click a contact to chat
                        </StatusBarField>
                    </StatusBar>
                }
            >
                <Stack pad="sm">
                    <Pane surface="sunken" pad="xs">
                        <Stack gap="sm">
                            <Expander title="Online" count={online.length}>
                                {online.map(row)}
                            </Expander>
                            <Expander title="Offline" count={offline.length}>
                                {offline.map(row)}
                            </Expander>
                        </Stack>
                    </Pane>
                </Stack>
            </WindowLayout>
        </Window>
    );
}

const transcript: Array<[string, string, boolean]> = [
    ['Ada', 'hey Grace :)', false],
    ['Ada', 'are you around tonight?', false],
    ['Grace', 'yes! what did you have in mind?', true],
    ['Ada', 'a very small picnic. bring the good mugs', false],
];

function ChatWindow({ active = true }: { active?: boolean }) {
    return (
        <Window
            skin="msn"
            title="Ada - Conversation"
            icon={<Icon name="chat" size={32} />}
            active={active}
            style={chatSize}
            onMinimise={fn()}
            onMaximise={fn()}
            onClose={fn()}
        >
            <WindowLayout
                scroll="none"
                contentLabel="Conversation"
                toolbar={
                    <Stack direction="row" gap="xs" pad="sm" align="center">
                        <Text tone="muted">To:</Text>
                        <Presence shape="buddy" status="online" />
                        <Text strong>Ada</Text>
                        <Text tone="muted">(Online)</Text>
                        <Stack.Item grow truncate>
                            <Text tone="muted" italic>
                                - reading by the window
                            </Text>
                        </Stack.Item>
                    </Stack>
                }
                statusBar={
                    <StatusBar>
                        <StatusBarField grow icon={<Icon name="edit" />}>
                            Ada is typing a message.
                        </StatusBarField>
                    </StatusBar>
                }
            >
                <Stack direction="row" gap="md" pad="md">
                    <Stack gap="sm" grow>
                        <ChatLog
                            aria-label="Conversation with Ada"
                            style={grow}
                        >
                            {transcript.map(([who, said, own], index) => (
                                <ChatMessage
                                    key={said}
                                    layout="transcript"
                                    sender={`${who} says:`}
                                    own={own}
                                    continued={
                                        index > 0 &&
                                        transcript[index - 1]?.[0] === who
                                    }
                                >
                                    {said}
                                </ChatMessage>
                            ))}
                        </ChatLog>
                        <Toolbar
                            variant="bare"
                            labels="none"
                            aria-label="Formatting"
                        >
                            <ToolbarButton
                                icon={<Icon name="emoticon" />}
                                hasMenu
                                aria-label="Select an emoticon"
                            />
                        </Toolbar>
                        <Stack direction="row" gap="sm">
                            <Stack.Item grow>
                                <TextArea
                                    rows={3}
                                    aria-label="Message to Ada"
                                    defaultValue="see you at eight"
                                    style={{
                                        width: '100%',
                                        resize: 'none',
                                    }}
                                />
                            </Stack.Item>
                            <CommandButton size="large" isDefault>
                                Send
                            </CommandButton>
                        </Stack>
                    </Stack>
                    <Stack justify="between">
                        <UserPicture src={picture(200)} size={96} />
                        <UserPicture src={picture(28)} size={96} />
                    </Stack>
                </Stack>
            </WindowLayout>
        </Window>
    );
}

const schemes: Scheme[] = ['luna-blue', 'olive-green', 'silver', 'noir'];

const inScheme = (scheme: Scheme, node: ReactNode) => (
    <div data-ui-scheme={scheme}>{node}</div>
);

function Pair() {
    return (
        <Stack direction="row" gap="xl" align="start">
            <ContactList />
            <ChatWindow active={false} />
        </Stack>
    );
}

const meta = {
    title: 'Skins/MSN',
    component: Pair,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <Desk size="large">
                <Story />
            </Desk>
        ),
    ],
} satisfies Meta<typeof Pair>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const ContactListWindow: Story = { render: () => <ContactList /> };

export const ChatWindowActive: Story = { render: () => <ChatWindow /> };

export const Inactive: Story = {
    render: () => (
        <Stack direction="row" gap="xl" align="start">
            <ContactList active={false} />
        </Stack>
    ),
};

export const CaptionHover: Story = {
    render: () => <ContactList />,
    play: preview('.ui-window-caption', 'hover'),
};

export const CaptionFocus: Story = {
    render: () => <ContactList />,
    play: preview('.ui-window-caption[data-caption="close"]', 'focus'),
};

export const AllSchemes: Story = {
    render: () => (
        <States
            layout="tiles"
            items={schemes.map((scheme) => [
                scheme,
                inScheme(scheme, <ContactList compact />),
            ])}
        />
    ),
    decorators: [
        (Story) => (
            <div className="ui-story-desk">
                <Story />
            </div>
        ),
    ],
};

export const Noir: Story = { ...noir };

export const NoirInactive: Story = {
    ...noir,
    render: () => <ContactList active={false} />,
};

export const ReducedMotion: Story = { ...reducedMotion };

export const ReducedTransparency: Story = { ...reducedTransparency };

export const IncreasedContrast: Story = {
    render: () => (
        <div data-ui-contrast="more" data-ui-scheme="luna-blue">
            <Pair />
        </div>
    ),
};
