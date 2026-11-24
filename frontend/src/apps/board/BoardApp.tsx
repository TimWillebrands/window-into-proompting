import {
    CommandButton,
    GroupBox,
    Hyperlink,
    Icon,
    PageHeader,
    Pane,
    Stack,
    Tabs,
    WindowLayout,
} from '@partytown/ui';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useGetWorldsPartyIdBoardSuspense } from '#api/party-zone';
import { useDesktopStore } from '#lib/desktop-context';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import Portrait from '../../components/Portrait';
import { usePlayerId, usePlayerName, WORLD_ID } from '../../lib/world';
import {
    involvesPlayer,
    ms,
    playerActivityAt,
    sortFeed,
    useBoardSeen,
    useNow,
} from './board-lib';
import Composer from './Composer';
import PostCard from './PostCard';
import { useBoardActions } from './useBoardActions';

const PAGE = 30;

interface BoardAppProps {
    threadId?: string;
    compose?: boolean;
}

type Tab = 'timeline' | 'you';

export default function BoardApp({ threadId, compose }: BoardAppProps) {
    const { windowId } = useWindowInstance();
    const setWindowProps = useDesktopStore((s) => s.setWindowProps);
    const playerId = usePlayerId();
    const playerName = usePlayerName();
    const { data } = useGetWorldsPartyIdBoardSuspense(WORLD_ID);
    const { post, reply, respect } = useBoardActions(playerId, playerName);
    const now = useNow();
    const scrollRef = useRef<HTMLDivElement>(null);
    const composerRef = useRef<HTMLTextAreaElement>(null);
    const [tab, setTab] = useState<Tab>('timeline');
    const [limit, setLimit] = useState(PAGE);

    const storedSince = useBoardSeen((s) => s.since);
    const catchUp = useBoardSeen((s) => s.catchUp);
    const [since] = useState(storedSince);
    useEffect(() => () => catchUp(Date.now()), [catchUp]);

    const threads = useMemo(() => sortFeed(data.data ?? []), [data.data]);
    const yours = useMemo(
        () =>
            threads.filter(
                (t) =>
                    involvesPlayer(t, playerId, playerName) ||
                    playerActivityAt(t, playerId) > 0,
            ),
        [threads, playerId, playerName],
    );
    const unseenForYou = yours.filter(
        (t) => playerActivityAt(t, playerId) > since,
    ).length;
    const names = useMemo(
        () => [
            ...new Set(
                threads.flatMap((t) =>
                    [
                        t.authorName,
                        ...(t.replies ?? []).map((r) => r.authorName),
                    ]
                        .filter((n): n is string => !!n)
                        .map((n) => n.split(' ')[0]),
                ),
            ),
        ],
        [threads],
    );
    const feed = tab === 'you' ? yours : threads;

    const [topSeenAt, setTopSeenAt] = useState(() =>
        Math.max(0, ...threads.map((t) => ms(t.lastActivityAt))),
    );
    const [atTop, setAtTop] = useState(true);
    const newest = Math.max(0, ...threads.map((t) => ms(t.lastActivityAt)));
    useEffect(() => {
        if (atTop) setTopSeenAt(newest);
    }, [atTop, newest]);
    const fresh = threads.filter(
        (t) =>
            ms(t.lastActivityAt) > topSeenAt &&
            (t.replies?.at(-1)?.authorId ?? t.authorId) !== playerId,
    ).length;

    const toTop = () =>
        scrollRef.current?.scrollTo({ top: 0, behavior: 'smooth' });

    const focusId =
        threadId && threads.some((t) => t.id === threadId)
            ? threadId
            : undefined;
    useEffect(() => {
        if (!threadId) return;
        setTab('timeline');
        const t = window.setTimeout(
            () =>
                setWindowProps(windowId, (p) => ({
                    ...p,
                    threadId: undefined,
                })),
            2500,
        );
        return () => window.clearTimeout(t);
    }, [threadId, setWindowProps, windowId]);
    useEffect(() => {
        if (!compose) return;
        composerRef.current?.focus();
        setWindowProps(windowId, (p) => ({ ...p, compose: undefined }));
    }, [compose, setWindowProps, windowId]);
    const shownLimit = Math.max(
        limit,
        feed.findIndex((t) => t.id === focusId) + 1,
    );

    const feedView = (
        <Pane
            ref={scrollRef}
            className="h-full"
            onScroll={(e) => setAtTop(e.currentTarget.scrollTop < 40)}
        >
            {fresh > 0 && !atTop && (
                <Stack
                    direction="row"
                    justify="center"
                    className="sticky top-2 z-10 h-0"
                >
                    <CommandButton
                        size="small"
                        icon={<Icon name="up" />}
                        onClick={toTop}
                    >
                        {fresh} new {fresh === 1 ? 'post' : 'posts'}
                    </CommandButton>
                </Stack>
            )}
            <Stack gap="lg" pad="md" className="mx-auto max-w-[620px]">
                <GroupBox title="Wie Wat Waar?">
                    <Composer
                        playerId={playerId}
                        textareaRef={composerRef}
                        placeholder="What are you up to?"
                        submitLabel="Post"
                        onSubmit={async (body) => {
                            await post(body);
                            setTab('timeline');
                            toTop();
                        }}
                    />
                </GroupBox>

                {feed.length === 0 ? (
                    <p className="p-4">
                        {tab === 'you'
                            ? 'Nothing for you yet. Post something, reply to someone, or hand out a Respect!.'
                            : 'Partyboard is quiet. Be the first to post something.'}
                    </p>
                ) : (
                    feed
                        .slice(0, shownLimit)
                        .map((t) => (
                            <PostCard
                                key={t.id}
                                thread={t}
                                playerId={playerId}
                                now={now}
                                since={since}
                                names={names}
                                focused={t.id === focusId}
                                onReply={reply}
                                onRespect={(id, replyId, given) =>
                                    void respect(id, replyId, given)
                                }
                            />
                        ))
                )}
                {feed.length > shownLimit && (
                    <Hyperlink
                        className="self-center"
                        onClick={() => setLimit(shownLimit + PAGE)}
                    >
                        Show older posts
                    </Hyperlink>
                )}
            </Stack>
        </Pane>
    );

    return (
        <WindowLayout scroll="none">
            <PageHeader
                icon={<Icon name="board" size={32} />}
                title="Partyboard"
                description="what the World is up to"
                aside={
                    playerId ? (
                        <>
                            <Portrait
                                who={playerId}
                                alt=""
                                width={24}
                                height={24}
                            />
                            <span className="truncate">
                                {playerName ?? 'you'}
                            </span>
                        </>
                    ) : undefined
                }
            />
            <Tabs
                label="Partyboard views"
                className="m-2 min-h-0 flex-1"
                value={tab}
                onValueChange={(next) => {
                    if (next === 'timeline' && tab === 'timeline') toTop();
                    setTab(next as Tab);
                }}
                items={[
                    {
                        value: 'timeline',
                        label: 'Timeline',
                        icon: <Icon name="board" />,
                        content: feedView,
                    },
                    {
                        value: 'you',
                        label:
                            unseenForYou > 0 ? `You (${unseenForYou})` : 'You',
                        icon: <Icon name="user" />,
                        content: feedView,
                    },
                ]}
            />
        </WindowLayout>
    );
}
