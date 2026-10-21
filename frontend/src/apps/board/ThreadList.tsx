import type { BoardThread } from '../../api/model/boardThread';
import {
    fullDate,
    isUnread,
    lastPost,
    ms,
    postCounts,
    relativeTime,
    repliesToPlayer,
    useBoardSeen,
    useNow,
} from './board-lib';

const HOT_REPLIES = 5;

interface Props {
    threads: BoardThread[];
    playerId: string | null;
    onOpen: (threadId: string) => void;
    onNewTopic: () => void;
}

/** Forum index: one topic per row, newest activity first. */
export default function ThreadList({
    threads,
    playerId,
    onOpen,
    onNewTopic,
}: Props) {
    const now = useNow();
    const { since, seen, markAllRead } = useBoardSeen();
    const counts = postCounts(threads);
    const totalPosts = [...counts.values()].reduce((a, b) => a + b, 0);
    const top = [...counts.entries()].sort((a, b) => b[1] - a[1])[0];
    const topName = top
        ? threads
              .flatMap((t) => [
                  { id: t.authorId, name: t.authorName },
                  ...(t.replies ?? []).map((r) => ({
                      id: r.authorId,
                      name: r.authorName,
                  })),
              ])
              .find((a) => a.id === top[0])?.name
        : null;

    return (
        <div className="flex flex-col gap-2">
            <div className="flex flex-wrap items-end justify-between gap-2">
                <button
                    type="button"
                    className="pb-btn pb-btn-primary"
                    onClick={onNewTopic}
                >
                    ✏️ New Topic
                </button>
                <button
                    type="button"
                    className="pb-link pb-small"
                    onClick={markAllRead}
                >
                    Mark all topics read
                </button>
            </div>

            <table className="pb-forumline">
                <thead>
                    <tr>
                        <th style={{ width: 30 }} aria-label="Status" />
                        <th>Topics</th>
                        <th className="pb-col-replies" style={{ width: 62 }}>
                            Replies
                        </th>
                        <th className="pb-col-author" style={{ width: 120 }}>
                            Author
                        </th>
                        <th className="pb-col-last" style={{ width: 150 }}>
                            Last Post
                        </th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td colSpan={5} className="pb-cat">
                            Partyboard — General Discussion
                        </td>
                    </tr>
                    {threads.length === 0 && (
                        <tr>
                            <td colSpan={5} className="pb-row1 text-center">
                                <div className="py-6">
                                    There are no posts in this forum yet.
                                    <br />
                                    <button
                                        type="button"
                                        className="pb-link"
                                        onClick={onNewTopic}
                                    >
                                        Click here to start the first topic
                                    </button>
                                </div>
                            </td>
                        </tr>
                    )}
                    {threads.map((t) => {
                        const id = t.id ?? '';
                        const replies = t.replies?.length ?? 0;
                        const unread = isUnread(t, playerId, since, seen);
                        const toYou = repliesToPlayer(t, playerId);
                        const mine = playerId && t.authorId === playerId;
                        const last = lastPost(t);
                        const lastAt = ms(t.lastActivityAt);
                        return (
                            <tr key={id}>
                                <td
                                    className="pb-row1 text-center"
                                    title={
                                        unread ? 'New posts' : 'No new posts'
                                    }
                                    style={{ fontSize: 16 }}
                                >
                                    {replies >= HOT_REPLIES
                                        ? '🔥'
                                        : unread
                                          ? '📂'
                                          : '📁'}
                                </td>
                                <td className="pb-row1">
                                    <div className="flex flex-wrap items-center gap-x-1.5 gap-y-0.5">
                                        {unread && (
                                            <span className="pb-badge pb-badge-new">
                                                NEW
                                            </span>
                                        )}
                                        <button
                                            type="button"
                                            className="pb-link pb-topictitle"
                                            onClick={() => onOpen(id)}
                                        >
                                            {t.title}
                                        </button>
                                        {toYou > 0 && (
                                            <span
                                                className="pb-badge pb-badge-you"
                                                title="Others replied to your topic"
                                            >
                                                ↩ {toYou}{' '}
                                                {toYou === 1
                                                    ? 'reply'
                                                    : 'replies'}{' '}
                                                to you
                                            </span>
                                        )}
                                    </div>
                                    <div className="pb-small pb-muted mt-0.5 truncate">
                                        {mine ? 'Your topic' : t.authorName}
                                        <span className="pb-col-last-inline">
                                            {' · '}
                                            {replies}{' '}
                                            {replies === 1
                                                ? 'reply'
                                                : 'replies'}{' '}
                                            · {relativeTime(lastAt, now)}
                                        </span>
                                    </div>
                                </td>
                                <td className="pb-row2 pb-col-replies text-center">
                                    {replies}
                                </td>
                                <td
                                    className={`pb-row1 pb-col-author text-center truncate pb-name ${mine ? 'pb-player-name' : ''}`}
                                >
                                    {t.authorName}
                                </td>
                                <td className="pb-row2 pb-col-last text-center pb-small">
                                    <div title={fullDate(lastAt)}>
                                        {relativeTime(lastAt, now)}
                                    </div>
                                    <div className="truncate">
                                        <span
                                            className={
                                                last.authorId === playerId
                                                    ? 'pb-player-name'
                                                    : ''
                                            }
                                        >
                                            {last.authorName}
                                        </span>{' '}
                                        <button
                                            type="button"
                                            className="pb-link"
                                            title="View latest post"
                                            onClick={() => onOpen(id)}
                                        >
                                            ➜
                                        </button>
                                    </div>
                                </td>
                            </tr>
                        );
                    })}
                </tbody>
            </table>

            <table className="pb-forumline">
                <tbody>
                    <tr>
                        <td className="pb-cat">Board statistics</td>
                    </tr>
                    <tr>
                        <td className="pb-row1 pb-small">
                            Our members have posted a total of{' '}
                            <b>{totalPosts}</b> articles in{' '}
                            <b>{threads.length}</b> topics.
                            {topName && (
                                <>
                                    {' '}
                                    Most prolific poster: <b>{topName}</b> (
                                    {top?.[1]} posts).
                                </>
                            )}
                        </td>
                    </tr>
                </tbody>
            </table>

            <div className="pb-small pb-muted flex flex-wrap gap-x-4 gap-y-1">
                <span>📂 New posts</span>
                <span>📁 No new posts</span>
                <span>🔥 Popular ({HOT_REPLIES}+ replies)</span>
                <span>
                    <span className="pb-badge pb-badge-you">↩ to you</span>{' '}
                    Replies on your topic
                </span>
            </div>
        </div>
    );
}
