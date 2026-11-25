import type { Meta, StoryObj } from '@storybook/react-vite';
import { type CSSProperties, type ReactNode, useState } from 'react';
import {
    noir,
    reducedMotion,
    reducedTransparency,
    stage,
} from '../../../story-kit/story-kit';
import { Text } from '../../controls/Text/Text';
import { Toolbar, ToolbarButton } from '../../controls/Toolbar/Toolbar';
import { Icon } from '../../icons';
import { ChatLog } from '../../views/ChatLog/ChatLog';
import { ChatMessage } from '../../views/ChatMessage/ChatMessage';
import { ListView } from '../../views/ListView/ListView';
import { GroupBox } from '../GroupBox/GroupBox';
import { Columns } from './Columns';

const meta = {
    title: 'Surfaces/Columns',
    component: Columns,
    decorators: [stage],
    parameters: { stage: { width: 'wide' } },
} satisfies Meta<typeof Columns>;

export default meta;

type Story = StoryObj<typeof meta>;

const box = (width: number): CSSProperties => ({ width, height: 240 });

const spots = Array.from({ length: 9 }, (_, index) => ({
    id: `spot-${index}`,
    label: ['Bar', 'Window seat', 'Sofa', 'Terrace'][index % 4],
    icon: <Icon name="chair" />,
}));

const lines = [
    ['Ada', 'who brought the lemon cake?'],
    ['Grace', 'that was me, there is more in the kitchen'],
    ['Ada', 'you are a hero'],
    ['Linus', 'save me a slice'],
] as const;

const spotsBox = (
    <GroupBox title="Spots" icon={<Icon name="chair" />} fill>
        <ListView label="Spots" view="list" items={spots} />
    </GroupBox>
);

const talkBox = (
    <GroupBox title="Said out loud here" icon={<Icon name="chat" />} fill>
        <ChatLog aria-label="Said out loud here">
            {lines.map(([sender, text]) => (
                <ChatMessage key={text} layout="transcript" sender={sender}>
                    {text}
                </ChatMessage>
            ))}
        </ChatLog>
    </GroupBox>
);

function Frame({ width, children }: { width: number; children: ReactNode }) {
    return (
        <div style={box(width)} className="ui-story-column">
            {children}
        </div>
    );
}

export const Default: Story = {
    render: () => (
        <Frame width={720}>
            <Columns grow>
                <Columns.Column weight={1.2}>{spotsBox}</Columns.Column>
                <Columns.Column>{talkBox}</Columns.Column>
            </Columns>
        </Frame>
    ),
};

export const Weights: Story = {
    render: () => (
        <Frame width={720}>
            <Columns grow gap="lg">
                <Columns.Column weight={0.85} minWidth="220px">
                    <GroupBox title="weight 0.85, at least 220px" fill>
                        <Text>The narrower column keeps its minimum.</Text>
                    </GroupBox>
                </Columns.Column>
                <Columns.Column weight={1.15}>
                    <GroupBox title="weight 1.15" fill>
                        <Text>The wider column takes the rest.</Text>
                    </GroupBox>
                </Columns.Column>
            </Columns>
        </Frame>
    ),
};

export const NarrowStacks: Story = {
    render: () => (
        <Frame width={400}>
            <Columns grow narrowBelow={520}>
                <Columns.Column weight={1.2}>{spotsBox}</Columns.Column>
                <Columns.Column>{talkBox}</Columns.Column>
            </Columns>
        </Frame>
    ),
};

function OneAtATime() {
    const [show, setShow] = useState(1);
    return (
        <Frame width={400}>
            <Columns
                grow
                narrow="one"
                narrowBelow={640}
                show={show}
                switcher={
                    <Toolbar aria-label="Show">
                        <ToolbarButton
                            icon={<Icon name="chair" />}
                            pressed={show === 0}
                            onClick={() => setShow(0)}
                        >
                            Spots
                        </ToolbarButton>
                        <ToolbarButton
                            icon={<Icon name="chat" />}
                            pressed={show === 1}
                            onClick={() => setShow(1)}
                        >
                            Talk
                        </ToolbarButton>
                    </Toolbar>
                }
            >
                <Columns.Column weight={1.2}>{spotsBox}</Columns.Column>
                <Columns.Column>{talkBox}</Columns.Column>
            </Columns>
        </Frame>
    );
}

export const NarrowShowsOne: Story = { render: () => <OneAtATime /> };

export const Noir: Story = { ...Default, ...noir };
export const NoirNarrowShowsOne: Story = { ...NarrowShowsOne, ...noir };
export const ReducedMotion: Story = { ...Default, ...reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    ...reducedTransparency,
};
