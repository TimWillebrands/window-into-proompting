import type { Meta, StoryObj } from '@storybook/react-vite';
import { CommandButton } from '../../controls';
import { Hyperlink } from '../../controls/Hyperlink/Hyperlink';
import { Icon } from '../../icons';
import { noir, reducedMotion, reducedTransparency, stage } from '../story-kit';
import { Post, PostMention } from './Post';

const meta = {
    title: 'Feed/Post',
    component: Post,
    decorators: [stage],
    args: {
        title: 'Ada Lovelace',
        meta: '5 min ago',
        children: 'Who is up for a walk along the canal tonight?',
    },
} satisfies Meta<typeof Post>;

export default meta;

type Story = StoryObj<typeof meta>;

const picture = <Icon name="user" size={48} />;

const actions = (count: number) => (
    <>
        <CommandButton size="small" icon={<Icon name="star" />}>
            Respect! {count}
        </CommandButton>
        <CommandButton size="small" icon={<Icon name="chat" />}>
            Reply
        </CommandButton>
    </>
);

const reply = (
    <Post
        variant="flat"
        size="compact"
        picture={<Icon name="user" size={32} />}
        title="Grace Hopper"
        meta="2 min ago"
        actions={
            <CommandButton size="small" icon={<Icon name="star" />}>
                Respect!
            </CommandButton>
        }
    >
        Count me in, <PostMention>Ada</PostMention>. Meet at the bridge?
    </Post>
);

const thread = (
    <Post
        picture={picture}
        title="Ada Lovelace"
        meta="5 min ago"
        actions={actions(3)}
        replies={
            <>
                <Hyperlink>Show 2 earlier replies</Hyperlink>
                {reply}
                <Hyperlink>Write a reply…</Hyperlink>
            </>
        }
    >
        Who is up for a walk along the canal tonight? The weather is finally
        turning.
    </Post>
);

export const Default: Story = {
    render: () => <div className="ui-story-feed">{thread}</div>,
};

export const Playground: Story = {
    render: (args) => (
        <div className="ui-story-feed">
            <Post {...args} picture={picture} actions={actions(0)} />
        </div>
    ),
};

export const States: Story = {
    render: () => (
        <div className="ui-story-feed">
            <Post picture={picture} title="Plain" meta="just now">
                An ordinary post.
            </Post>
            <Post picture={picture} title="Unread" meta="1 min ago" unread>
                Something you have not seen yet.
            </Post>
            <Post picture={picture} title="Current" meta="3 min ago" current>
                The post a notification pointed at.
            </Post>
            <Post
                picture={picture}
                title="With context"
                meta="1 h ago"
                context={
                    <>
                        <Icon name="undo" /> Grace replied · 2 min ago
                    </>
                }
            >
                A post brought up by a reply.
            </Post>
            <Post title="No picture" meta="yesterday">
                A post without a picture.
            </Post>
        </div>
    ),
};

export const Flat: Story = {
    render: () => (
        <div className="ui-story-feed">
            <Post variant="flat" title="Mon 3 Nov, 14:00" meta="mood: calm">
                Walked to the market. The baker remembered my name.
            </Post>
            <Post variant="flat" title="Tue 4 Nov, 09:30" meta="mood: restless">
                Couldn't sleep. Wrote a letter I won't send.
            </Post>
            <Post variant="flat" unread title="Wed 5 Nov, 21:10">
                New entry.
            </Post>
        </div>
    ),
};

export const Narrow: Story = {
    render: () => (
        <div className="ui-story-feed ui-story-feed-narrow">{thread}</div>
    ),
};

export const Noir: Story = { ...Default, parameters: noir };
export const ReducedMotion: Story = { ...Default, parameters: reducedMotion };
export const ReducedTransparency: Story = {
    ...Default,
    parameters: reducedTransparency,
};
