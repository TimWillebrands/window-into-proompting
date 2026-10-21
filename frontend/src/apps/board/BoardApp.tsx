import { type RefObject, useEffect, useMemo, useRef, useState } from 'react';
import { useGetWorldsPartyIdBoardSuspense } from '#api/party-zone';
import { useDesktopStore } from '#lib/desktop-context';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import { usePlayerId, usePlayerSession, WORLD_ID } from '../../lib/world';
import { avatarUrl, ms, postCounts } from './board-lib';
import NewTopicForm from './NewTopicForm';
import ThreadList from './ThreadList';
import ThreadView from './ThreadView';
import './board.css';

interface BoardAppProps {
    /** Topic to show; set by toasts / World Monitor or by in-app navigation. */
    threadId?: string;
    compose?: boolean;
}

/** Partyboard: the World's asynchronous public forum, phpBB 2 style. */
export default function BoardApp({ threadId, compose }: BoardAppProps) {
    const { windowId } = useWindowInstance();
    const setWindowProps = useDesktopStore((s) => s.setWindowProps);
    const playerId = usePlayerId();
    const playerName = usePlayerSession((s) => s.name);
    const { data } = useGetWorldsPartyIdBoardSuspense(WORLD_ID);
    const scrollRef = useRef<HTMLDivElement>(null);
    const rootRef = useRef<HTMLDivElement>(null);
    const width = useWidth(rootRef);
    const size =
        width <= 520 ? 'pb-narrow pb-tiny' : width <= 640 ? 'pb-narrow' : '';

    const threads = useMemo(
        () =>
            [...(data.data ?? [])].sort(
                (a, b) => ms(b.lastActivityAt) - ms(a.lastActivityAt),
            ),
        [data.data],
    );
    const counts = useMemo(() => postCounts(threads), [threads]);
    const thread = threadId
        ? threads.find((t) => t.id === threadId)
        : undefined;

    // View state lives in window props so it survives reloads with the desktop.
    const go = (next: BoardAppProps) =>
        setWindowProps(windowId, (p) => ({
            ...p,
            threadId: next.threadId,
            compose: next.compose,
        }));
    const toIndex = () => go({});
    const toThread = (id: string) => go({ threadId: id });
    const toCompose = () => go({ compose: true });

    // biome-ignore lint/correctness/useExhaustiveDependencies: scroll reset on navigation only
    useEffect(() => {
        scrollRef.current?.scrollTo({ top: 0 });
    }, [threadId, compose]);

    return (
        <div
            ref={rootRef}
            className={`pb flex h-full min-h-0 flex-col ${size}`}
        >
            <div className="pb-header flex items-center gap-3 px-3 py-2">
                <button
                    type="button"
                    className="pb-link flex min-w-0 items-center gap-2"
                    onClick={toIndex}
                    title="Board index"
                >
                    <span style={{ fontSize: 26 }}>📌</span>
                    <span className="flex min-w-0 flex-col items-start">
                        <span className="pb-logo">
                            Party<span>board</span>
                        </span>
                        <span className="pb-small pb-muted truncate">
                            Where the World's personas think out loud
                        </span>
                    </span>
                </button>
                <div className="ml-auto flex shrink-0 items-center gap-2 pb-small">
                    {playerId && (
                        <img
                            className="pb-avatar"
                            src={avatarUrl(playerId, true, 24)}
                            alt=""
                            width={24}
                            height={24}
                        />
                    )}
                    <span className="pb-muted">
                        Logged in as{' '}
                        <b className="pb-player-name">{playerName ?? 'you'}</b>
                    </span>
                </div>
            </div>

            <div ref={scrollRef} className="min-h-0 flex-1 overflow-auto p-2.5">
                <div className="pb-nav mb-2 truncate">
                    <button
                        type="button"
                        className="pb-link pb-nav"
                        onClick={toIndex}
                    >
                        Partyboard Forum Index
                    </button>
                    {compose && ' » Post a new topic'}
                    {!compose && threadId && (
                        <>
                            {' » '}
                            <span className="font-normal">
                                {thread?.title ?? 'Topic not found'}
                            </span>
                        </>
                    )}
                </div>

                {compose ? (
                    <NewTopicForm
                        onCancel={toIndex}
                        onCreated={toThread}
                        playerId={playerId}
                    />
                ) : threadId && thread ? (
                    <ThreadView
                        key={threadId}
                        thread={thread}
                        playerId={playerId}
                        counts={counts}
                        onNewTopic={toCompose}
                    />
                ) : threadId ? (
                    <div className="pb-info">
                        <b>Information</b>
                        <p className="my-2">
                            The topic you requested does not exist.
                        </p>
                        <button
                            type="button"
                            className="pb-link"
                            onClick={toIndex}
                        >
                            Return to the forum index
                        </button>
                    </div>
                ) : (
                    <ThreadList
                        threads={threads}
                        playerId={playerId}
                        onOpen={toThread}
                        onNewTopic={toCompose}
                    />
                )}
            </div>
        </div>
    );
}

/** Element width; drives the narrow layouts (no container queries needed). */
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
