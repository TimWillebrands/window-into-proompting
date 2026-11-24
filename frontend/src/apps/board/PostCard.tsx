import { CommandButton, Hyperlink, Icon, Post, Text } from '@partytown/ui';
import { Fragment, type ReactNode, useEffect, useRef, useState } from 'react';
import type { BoardReply } from '../../api/model/boardReply';
import type { BoardThread } from '../../api/model/boardThread';
import Portrait from '../../components/Portrait';
import { bumpedBy, fullDate, isNewLine, ms, relativeTime } from './board-lib';
import Composer from './Composer';
import RespectButton from './RespectButton';

const VISIBLE_REPLIES = 2;

interface Props {
    thread: BoardThread;
    playerId: string | null;
    now: number;
    since: number;
    names: string[];
    focused: boolean;
    onReply: (threadId: string, body: string) => Promise<unknown>;
    onRespect: (
        threadId: string,
        replyId: string | null,
        given: boolean,
    ) => void;
}

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
        <Post
            ref={cardRef}
            aria-label={`Post by ${thread.authorName}`}
            current={focused}
            unread={isNewLine(thread, playerId, since)}
            picture={<Portrait who={thread.authorId ?? ''} alt="" />}
            title={authorTitle(
                thread.authorName ?? '?',
                thread.authorId,
                playerId,
            )}
            meta={<When at={ms(thread.createdAt)} now={now} />}
            context={
                bump && bump.authorId !== thread.authorId ? (
                    <>
                        <Icon name="undo" />
                        {bump.authorId === playerId ? 'You' : bump.authorName}{' '}
                        replied · {relativeTime(ms(bump.createdAt), now)}
                    </>
                ) : undefined
            }
            actions={
                <>
                    <RespectButton
                        respect={thread.respect}
                        playerId={playerId}
                        own={mine}
                        onToggle={(given) => onRespect(id, null, given)}
                    />
                    <CommandButton
                        size="small"
                        icon={<Icon name="chat" />}
                        pressed={replying}
                        onClick={() => setReplying((r) => !r)}
                    >
                        Reply{replies.length > 0 ? ` · ${replies.length}` : ''}
                    </CommandButton>
                </>
            }
            replies={
                replies.length > 0 || replying ? (
                    <>
                        {hidden > 0 && (
                            <Hyperlink
                                className="justify-self-start"
                                onClick={() => setExpanded(true)}
                            >
                                Show {hidden} earlier{' '}
                                {hidden === 1 ? 'reply' : 'replies'}
                            </Hyperlink>
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
                                <Hyperlink
                                    className="justify-self-start"
                                    onClick={() => setReplying(true)}
                                >
                                    Write a reply…
                                </Hyperlink>
                            )
                        )}
                    </>
                ) : undefined
            }
        >
            {withMentions(thread.body ?? '', names)}
        </Post>
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
        <Post
            variant="flat"
            size="compact"
            unread={isNew}
            picture={<Portrait who={reply.authorId ?? ''} alt="" />}
            title={authorTitle(
                reply.authorName ?? '?',
                reply.authorId,
                playerId,
            )}
            meta={<When at={ms(reply.createdAt)} now={now} />}
            actions={
                <RespectButton
                    respect={reply.respect}
                    playerId={playerId}
                    own={!!playerId && reply.authorId === playerId}
                    onToggle={onRespect}
                />
            }
        >
            {withMentions(reply.body ?? '', names)}
        </Post>
    );
}

function authorTitle(
    name: string,
    authorId: string | null | undefined,
    playerId: string | null,
) {
    return !!playerId && authorId === playerId ? `${name} (you)` : name;
}

function When({ at, now }: { at: number; now: number }) {
    return (
        <time dateTime={new Date(at).toISOString()} title={fullDate(at)}>
            {relativeTime(at, now)}
        </time>
    );
}

export function withMentions(text: string, names: string[]): ReactNode {
    const words = names.filter((n) => n.length > 1);
    if (words.length === 0) return text;
    const escaped = words.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
    const parts = text.split(new RegExp(`\\b(${escaped.join('|')})\\b`, 'gi'));
    return parts.map((part, i) =>
        i % 2 === 1 ? (
            // biome-ignore lint/suspicious/noArrayIndexKey: split output is positional
            <Text key={i} as="strong" tone="neutral" strong>
                {part}
            </Text>
        ) : (
            // biome-ignore lint/suspicious/noArrayIndexKey: split output is positional
            <Fragment key={i}>{part}</Fragment>
        ),
    );
}
