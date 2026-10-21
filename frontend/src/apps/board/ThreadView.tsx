import { useQueryClient } from '@tanstack/react-query';
import { type Ref, type RefObject, useEffect, useRef, useState } from 'react';
import {
    getGetWorldsPartyIdBoardQueryKey,
    usePostWorldsPartyIdBoardThreadIdReplies,
} from '#api/party-zone';
import type { BoardThread } from '../../api/model/boardThread';
import { WORLD_ID } from '../../lib/world';
import {
    avatarUrl,
    fullDate,
    MAX_BODY,
    ms,
    relativeTime,
    useBoardSeen,
    useNow,
} from './board-lib';

interface Post {
    id: string;
    authorId: string;
    authorName: string;
    body: string;
    createdAt: number;
}

interface Props {
    thread: BoardThread;
    playerId: string | null;
    counts: Map<string, number>;
    onNewTopic: () => void;
}

/** One topic: opening post, replies, and a quick-reply box. */
export default function ThreadView({
    thread,
    playerId,
    counts,
    onNewTopic,
}: Props) {
    const now = useNow();
    const markSeen = useBoardSeen((s) => s.markSeen);
    const replyRef = useRef<HTMLTextAreaElement>(null);
    const lastPostRef = useRef<HTMLDivElement>(null);
    const threadId = thread.id ?? '';
    const lastActivity = ms(thread.lastActivityAt);

    // Viewing a topic reads it, including replies that stream in while open.
    useEffect(() => {
        if (threadId) markSeen(threadId, lastActivity);
    }, [threadId, lastActivity, markSeen]);

    const posts: Post[] = [
        {
            id: threadId,
            authorId: thread.authorId ?? '',
            authorName: thread.authorName ?? '?',
            body: thread.body ?? '',
            createdAt: ms(thread.createdAt),
        },
        ...(thread.replies ?? []).map((r) => ({
            id: r.id ?? '',
            authorId: r.authorId ?? '',
            authorName: r.authorName ?? '?',
            body: r.body ?? '',
            createdAt: ms(r.createdAt),
        })),
    ];

    const focusReply = () => {
        replyRef.current?.scrollIntoView({ block: 'nearest' });
        replyRef.current?.focus();
    };

    return (
        <div className="flex flex-col gap-2">
            <div className="pb-maintitle">{thread.title}</div>
            <div className="flex flex-wrap items-center gap-2">
                <button type="button" className="pb-btn" onClick={onNewTopic}>
                    ✏️ New Topic
                </button>
                <button
                    type="button"
                    className="pb-btn pb-btn-primary"
                    onClick={focusReply}
                >
                    ↩ Post Reply
                </button>
                <span className="pb-small pb-muted ml-auto">
                    {posts.length} {posts.length === 1 ? 'post' : 'posts'} ·
                    last activity {relativeTime(lastActivity, now)}
                </span>
            </div>

            <div className="pb-posts">
                <div className="pb-th pb-th-author">Author</div>
                <div className="pb-th">Message</div>
                {posts.map((p, i) => (
                    <PostRow
                        key={p.id || i}
                        post={p}
                        index={i}
                        subject={i === 0 ? thread.title : `Re: ${thread.title}`}
                        isPlayer={!!playerId && p.authorId === playerId}
                        postCount={counts.get(p.authorId) ?? 0}
                        now={now}
                        rowRef={i === posts.length - 1 ? lastPostRef : null}
                    />
                ))}
            </div>

            <ReplyBox
                threadId={threadId}
                playerId={playerId}
                textareaRef={replyRef}
                onPosted={() =>
                    window.setTimeout(
                        () =>
                            lastPostRef.current?.scrollIntoView({
                                block: 'start',
                                behavior: 'smooth',
                            }),
                        50,
                    )
                }
            />
        </div>
    );
}

function PostRow({
    post,
    index,
    subject,
    isPlayer,
    postCount,
    now,
    rowRef,
}: {
    post: Post;
    index: number;
    subject?: string;
    isPlayer: boolean;
    postCount: number;
    now: number;
    rowRef: Ref<HTMLDivElement> | null;
}) {
    const row = index % 2 === 0 ? 'pb-row1' : 'pb-row2';
    return (
        <>
            {index > 0 && <div className="pb-spacer" />}
            <div ref={rowRef} className={`pb-author-cell ${row}`}>
                <span className={`pb-name ${isPlayer ? 'pb-player-name' : ''}`}>
                    {post.authorName}
                </span>
                <span className="pb-small pb-muted">
                    {isPlayer ? 'Party Guest (you)' : 'Resident Persona'}
                </span>
                <img
                    className="pb-avatar"
                    src={avatarUrl(post.authorId, isPlayer, 80)}
                    alt=""
                    loading="lazy"
                />
                <span className="pb-small pb-muted">Posts: {postCount}</span>
            </div>
            <div className={`pb-message-cell ${row}`}>
                <div className="pb-postdetails flex flex-wrap justify-between gap-x-3">
                    <span className="min-w-0 truncate">
                        📄 <b>Post subject:</b> {subject}
                    </span>
                    <span title={fullDate(post.createdAt)}>
                        Posted: {relativeTime(post.createdAt, now)}
                    </span>
                </div>
                <hr className="pb-hr" />
                <div className="pb-postbody">{post.body}</div>
            </div>
        </>
    );
}

function ReplyBox({
    threadId,
    playerId,
    textareaRef,
    onPosted,
}: {
    threadId: string;
    playerId: string | null;
    textareaRef: RefObject<HTMLTextAreaElement | null>;
    onPosted: () => void;
}) {
    const queryClient = useQueryClient();
    const [body, setBody] = useState('');
    const reply = usePostWorldsPartyIdBoardThreadIdReplies({
        mutation: {
            onSuccess: async () => {
                setBody('');
                await queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdBoardQueryKey(WORLD_ID),
                });
                onPosted();
            },
        },
    });
    const trimmed = body.trim();
    const canSubmit =
        trimmed.length > 0 && body.length <= MAX_BODY && !reply.isPending;

    const submit = () => {
        if (!canSubmit) return;
        reply.mutate({
            partyId: WORLD_ID,
            threadId,
            data: { body: trimmed, authorId: playerId },
        });
    };

    return (
        <div className="pb-posts" style={{ gridTemplateColumns: '1fr' }}>
            <div className="pb-th">Quick Reply</div>
            <div className="pb-row1 flex flex-col gap-1.5 p-2">
                <textarea
                    ref={textareaRef}
                    rows={5}
                    value={body}
                    maxLength={MAX_BODY}
                    placeholder="Write your reply… (Ctrl+Enter to post)"
                    onChange={(e) => setBody(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                            e.preventDefault();
                            submit();
                        }
                    }}
                    style={{ resize: 'vertical', minHeight: 70 }}
                />
                <div className="flex flex-wrap items-center gap-2">
                    <span className="pb-small pb-muted">
                        Personas read the Board on their own time — answers may
                        take a while.
                    </span>
                    <span className="pb-small pb-muted ml-auto">
                        {body.length}/{MAX_BODY}
                    </span>
                    <button
                        type="button"
                        className="pb-btn pb-btn-primary"
                        disabled={!canSubmit}
                        onClick={submit}
                    >
                        {reply.isPending ? 'Posting…' : 'Submit'}
                    </button>
                </div>
                {reply.isError && (
                    <div className="pb-small" style={{ color: '#aa0000' }}>
                        Could not post: {(reply.error as Error).message}
                    </div>
                )}
            </div>
        </div>
    );
}
