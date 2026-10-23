import { Fragment, type ReactNode, useEffect, useRef, useState } from 'react';
import type { BoardReply } from '../../api/model/boardReply';
import type { BoardThread } from '../../api/model/boardThread';
import {
    avatarUrl,
    bumpedBy,
    fullDate,
    isNewLine,
    ms,
    relativeTime,
} from './board-lib';
import Composer from './Composer';
import RespectButton from './RespectButton';

/** Replies shown before "show earlier replies". */
const VISIBLE_REPLIES = 2;

interface Props {
    thread: BoardThread;
    playerId: string | null;
    now: number;
    /** Lines by others newer than this are highlighted as new. */
    since: number;
    /** First names to highlight as mentions. */
    names: string[];
    /** Deep-linked from a toast: scroll here, open everything, flash. */
    focused: boolean;
    onReply: (threadId: string, body: string) => Promise<unknown>;
    onRespect: (
        threadId: string,
        replyId: string | null,
        given: boolean,
    ) => void;
}

/** One post on the timeline with its replies underneath. */
export default function PostCard({
    thread,
    playerId,
    now,
    since,
    names,
    focused,
    onReply,
    onRespect,
}: Props) {
    const id = thread.id ?? '';
    const replies = thread.replies ?? [];
    const newReplies = replies.filter((r) =>
        isNewLine(r, playerId, since),
    ).length;
    const [expanded, setExpanded] = useState(
        focused || newReplies > VISIBLE_REPLIES,
    );
    const [replying, setReplying] = useState(false);
    const cardRef = useRef<HTMLElement>(null);
    const mine = !!playerId && thread.authorId === playerId;
    const bump = bumpedBy(thread);
    const hidden = expanded ? 0 : Math.max(0, replies.length - VISIBLE_REPLIES);
    const shown = replies.slice(hidden);

    useEffect(() => {
        if (!focused) return;
        setExpanded(true);
        cardRef.current?.scrollIntoView({ block: 'start', behavior: 'smooth' });
    }, [focused]);

    return (
        <article
            ref={cardRef}
            className={`pb-card ${focused ? 'pb-flash' : ''} ${isNewLine(thread, playerId, since) ? 'pb-new' : ''}`}
            aria-label={`Post by ${thread.authorName}`}
        >
            {bump && bump.authorId !== thread.authorId && (
                <div className="pb-bump">
                    ↩ {bump.authorId === playerId ? 'You' : bump.authorName}{' '}
                    replied · {relativeTime(ms(bump.createdAt), now)}
                </div>
            )}
            <Line
                authorId={thread.authorId ?? ''}
                authorName={thread.authorName ?? '?'}
                createdAt={ms(thread.createdAt)}
                body={thread.body ?? ''}
                playerId={playerId}
                now={now}
                names={names}
                size={44}
            >
                <RespectButton
                    respect={thread.respect}
                    playerId={playerId}
                    own={mine}
                    onToggle={(given) => onRespect(id, null, given)}
                />
                <button
                    type="button"
                    className="pb-action"
                    onClick={() => setReplying((r) => !r)}
                >
                    💬 Reply{replies.length > 0 ? ` · ${replies.length}` : ''}
                </button>
            </Line>

            {(replies.length > 0 || replying) && (
                <div className="pb-replies">
                    {hidden > 0 && (
                        <button
                            type="button"
                            className="pb-more"
                            onClick={() => setExpanded(true)}
                        >
                            Show {hidden} earlier{' '}
                            {hidden === 1 ? 'reply' : 'replies'}
                        </button>
                    )}
                    {shown.map((r) => (
                        <ReplyLine
                            key={r.id}
                            reply={r}
                            playerId={playerId}
                            now={now}
                            names={names}
                            isNew={isNewLine(r, playerId, since)}
                            onRespect={(given) =>
                                onRespect(id, r.id ?? null, given)
                            }
                        />
                    ))}
                    {replying ? (
                        <Composer
                            compact
                            autoFocus
                            playerId={playerId}
                            placeholder={`Reply to ${mine ? 'your post' : thread.authorName}…`}
                            submitLabel="Reply"
                            onCancel={() => setReplying(false)}
                            onSubmit={async (body) => {
                                await onReply(id, body);
                                setExpanded(true);
                            }}
                        />
                    ) : (
                        replies.length > 0 && (
                            <button
                                type="button"
                                className="pb-reply-prompt"
                                onClick={() => setReplying(true)}
                            >
                                Write a reply…
                            </button>
                        )
                    )}
                </div>
            )}
        </article>
    );
}

function ReplyLine({
    reply,
    playerId,
    now,
    names,
    isNew,
    onRespect,
}: {
    reply: BoardReply;
    playerId: string | null;
    now: number;
    names: string[];
    isNew: boolean;
    onRespect: (given: boolean) => void;
}) {
    return (
        <div className={`pb-reply ${isNew ? 'pb-new' : ''}`}>
            <Line
                authorId={reply.authorId ?? ''}
                authorName={reply.authorName ?? '?'}
                createdAt={ms(reply.createdAt)}
                body={reply.body ?? ''}
                playerId={playerId}
                now={now}
                names={names}
                size={30}
            >
                <RespectButton
                    small
                    respect={reply.respect}
                    playerId={playerId}
                    own={!!playerId && reply.authorId === playerId}
                    onToggle={onRespect}
                />
            </Line>
        </div>
    );
}

/** Avatar, name, time, text, then the actions row. */
function Line({
    authorId,
    authorName,
    createdAt,
    body,
    playerId,
    now,
    names,
    size,
    children,
}: {
    authorId: string;
    authorName: string;
    createdAt: number;
    body: string;
    playerId: string | null;
    now: number;
    names: string[];
    size: number;
    children: ReactNode;
}) {
    const isPlayer = !!playerId && authorId === playerId;
    return (
        <div className="pb-line">
            <img
                className="pb-avatar"
                src={avatarUrl(authorId, isPlayer, size)}
                alt=""
                width={size}
                height={size}
            />
            <div className="min-w-0 flex-1">
                <div className="pb-meta">
                    <span
                        className={`pb-name ${isPlayer ? 'pb-player-name' : ''}`}
                    >
                        {authorName}
                    </span>
                    {isPlayer && <span className="pb-you">you</span>}
                    <span className="pb-muted">·</span>
                    <time
                        className="pb-muted"
                        dateTime={new Date(createdAt).toISOString()}
                        title={fullDate(createdAt)}
                    >
                        {relativeTime(createdAt, now)}
                    </time>
                </div>
                <p className="pb-body">{withMentions(body, names)}</p>
                <div className="pb-actions">{children}</div>
            </div>
        </div>
    );
}

/** Highlight first names of people in the World, like an @mention. */
export function withMentions(text: string, names: string[]): ReactNode {
    const words = names.filter((n) => n.length > 1);
    if (words.length === 0) return text;
    const escaped = words.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
    const parts = text.split(new RegExp(`\\b(${escaped.join('|')})\\b`, 'gi'));
    return parts.map((part, i) =>
        i % 2 === 1 ? (
            // biome-ignore lint/suspicious/noArrayIndexKey: split output is positional
            <span key={i} className="pb-mention">
                {part}
            </span>
        ) : (
            // biome-ignore lint/suspicious/noArrayIndexKey: split output is positional
            <Fragment key={i}>{part}</Fragment>
        ),
    );
}
