import {
    type ReactNode,
    type RefObject,
    useEffect,
    useMemo,
    useRef,
    useState,
} from 'react';
import { useGetWorldsPartyIdBoardSuspense } from '#api/party-zone';
import { useDesktopStore } from '#lib/desktop-context';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import { usePlayerId, usePlayerSession, WORLD_ID } from '../../lib/world';
import {
    avatarUrl,
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
import './board.css';

/** Posts rendered before "show older posts". */
const PAGE = 30;

interface BoardAppProps {
    /** Post to show; set by toasts / World Monitor. */
    threadId?: string;
    /** Focus the composer on open. */
    compose?: boolean;
}

type Tab = 'timeline' | 'you';

/** Partyboard: the World's social feed. Hyves on the outside, a timeline on the inside. */
export default function BoardApp({ threadId, compose }: BoardAppProps) {
    const { windowId } = useWindowInstance();
    const setWindowProps = useDesktopStore((s) => s.setWindowProps);
    const playerId = usePlayerId();
    const playerName = usePlayerSession((s) => s.name) ?? null;
    const { data } = useGetWorldsPartyIdBoardSuspense(WORLD_ID);
    const { post, reply, respect } = useBoardActions(playerId, playerName);
    const now = useNow();
    const rootRef = useRef<HTMLDivElement>(null);
    const scrollRef = useRef<HTMLDivElement>(null);
    const composerRef = useRef<HTMLTextAreaElement>(null);
    const width = useWidth(rootRef);
    const [tab, setTab] = useState<Tab>('timeline');
    const [limit, setLimit] = useState(PAGE);

    // Highlight against where you were when you opened the app; catch up when you leave.
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

    // "N new posts" pill: activity that arrived while you were scrolled down.
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

    // Deep link: show it on the timeline, then let go so the window can be used normally.
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

    return (
        <div
            ref={rootRef}
            className={`pb flex h-full min-h-0 flex-col ${width <= 520 ? 'pb-narrow' : ''}`}
        >
            <header className="pb-header">
                <div className="flex items-center gap-3 px-3 pt-2 pb-1.5">
                    <button
                        type="button"
                        className="pb-logo"
                        onClick={() => {
                            setTab('timeline');
                            toTop();
                        }}
                        title="Back to the top"
                    >
                        party<span>board</span>
                    </button>
                    <span className="pb-tagline truncate">
                        what the World is up to
                    </span>
                    {playerId && (
                        <span className="pb-me ml-auto">
                            <img
                                className="pb-avatar"
                                src={avatarUrl(playerId, true, 24)}
                                alt=""
                                width={24}
                                height={24}
                            />
                            <span className="truncate">
                                {playerName ?? 'you'}
                            </span>
                        </span>
                    )}
                </div>
                <nav className="pb-tabs" aria-label="Partyboard views">
                    <TabButton
                        active={tab === 'timeline'}
                        onClick={() => setTab('timeline')}
                    >
                        Timeline
                    </TabButton>
                    <TabButton
                        active={tab === 'you'}
                        onClick={() => setTab('you')}
                    >
                        You
                        {unseenForYou > 0 && (
                            <span className="pb-tab-badge">{unseenForYou}</span>
                        )}
                    </TabButton>
                </nav>
            </header>

            <div
                ref={scrollRef}
                className="relative min-h-0 flex-1 overflow-auto"
                onScroll={(e) => setAtTop(e.currentTarget.scrollTop < 40)}
            >
                {fresh > 0 && !atTop && (
                    <button type="button" className="pb-pill" onClick={toTop}>
                        ↑ {fresh} new {fresh === 1 ? 'post' : 'posts'}
                    </button>
                )}
                <div className="pb-column">
                    <section className="pb-box">
                        <h2 className="pb-box-title">Wie Wat Waar?</h2>
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
                    </section>

                    {feed.length === 0 ? (
                        <div className="pb-empty">
                            {tab === 'you'
                                ? 'Nothing for you yet. Post something, reply to someone, or hand out a Respect!.'
                                : 'Partyboard is quiet. Be the first to post something.'}
                        </div>
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
                        <button
                            type="button"
                            className="pb-btn pb-older"
                            onClick={() => setLimit(shownLimit + PAGE)}
                        >
                            Show older posts
                        </button>
                    )}
                </div>
            </div>
        </div>
    );
}

function TabButton({
    active,
    onClick,
    children,
}: {
    active: boolean;
    onClick: () => void;
    children: ReactNode;
}) {
    return (
        <button
            type="button"
            className={`pb-tab ${active ? 'pb-tab-on' : ''}`}
            aria-current={active ? 'page' : undefined}
            onClick={onClick}
        >
            {children}
        </button>
    );
}

/** Element width; drives the narrow layout (no container queries needed). */
function useWidth(ref: RefObject<HTMLElement | null>) {
    const [width, setWidth] = useState(Number.POSITIVE_INFINITY);
    useEffect(() => {
        const el = ref.current;
        if (!el) return;
        const ro = new ResizeObserver(([entry]) =>
            setWidth(entry.contentRect.width),
        );
        ro.observe(el);
        return () => ro.disconnect();
    }, [ref]);
    return width;
}
