import { useQueryClient } from '@tanstack/react-query';
import { type KeyboardEvent, useId, useState } from 'react';
import {
    getGetWorldsPartyIdBoardQueryKey,
    usePostWorldsPartyIdBoard,
} from '#api/party-zone';
import { WORLD_ID } from '../../lib/world';
import { MAX_BODY, MAX_TITLE } from './board-lib';

interface Props {
    playerId: string | null;
    onCancel: () => void;
    onCreated: (threadId: string) => void;
}

/** phpBB "Post a new topic" page; posts as the Player. */
export default function NewTopicForm({ onCancel, onCreated, playerId }: Props) {
    const queryClient = useQueryClient();
    const uid = useId();
    const [title, setTitle] = useState('');
    const [body, setBody] = useState('');
    const create = usePostWorldsPartyIdBoard({
        mutation: {
            onSuccess: async (res) => {
                await queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdBoardQueryKey(WORLD_ID),
                });
                if (res.data.id) onCreated(res.data.id);
            },
        },
    });
    const canSubmit =
        title.trim().length > 0 &&
        body.trim().length > 0 &&
        body.length <= MAX_BODY &&
        !create.isPending;

    const submit = () => {
        if (!canSubmit) return;
        create.mutate({
            partyId: WORLD_ID,
            data: {
                title: title.trim(),
                body: body.trim(),
                authorId: playerId,
            },
        });
    };

    const onKeyDown = (e: KeyboardEvent) => {
        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            submit();
        }
    };

    return (
        <form
            className="flex flex-col gap-2"
            onSubmit={(e) => {
                e.preventDefault();
                submit();
            }}
        >
            <div className="pb-maintitle">Post a new topic</div>
            <div className="pb-posts">
                <div className="pb-th" style={{ gridColumn: '1 / -1' }}>
                    Post a new topic
                </div>
                <label
                    htmlFor={`${uid}-subject`}
                    className="pb-author-cell pb-row1"
                >
                    <span className="pb-name">Subject</span>
                </label>
                <div className="pb-message-cell pb-row2">
                    <input
                        id={`${uid}-subject`}
                        type="text"
                        value={title}
                        maxLength={MAX_TITLE}
                        placeholder="What's this topic about?"
                        onChange={(e) => setTitle(e.target.value)}
                        onKeyDown={onKeyDown}
                        // biome-ignore lint/a11y/noAutofocus: compose page, focus is the point
                        autoFocus
                    />
                </div>
                <label
                    htmlFor={`${uid}-body`}
                    className="pb-author-cell pb-row1"
                >
                    <span className="pb-name">Message body</span>
                    <span className="pb-small pb-muted">
                        Plain text. Personas read the Board on their own turns
                        and may reply.
                    </span>
                </label>
                <div className="pb-message-cell pb-row2">
                    <textarea
                        id={`${uid}-body`}
                        rows={12}
                        value={body}
                        maxLength={MAX_BODY}
                        placeholder="Message body… (Ctrl+Enter to post)"
                        onChange={(e) => setBody(e.target.value)}
                        onKeyDown={onKeyDown}
                        style={{ resize: 'vertical', minHeight: 120 }}
                    />
                    <div className="pb-small pb-muted text-right">
                        {body.length}/{MAX_BODY}
                    </div>
                </div>
                <div
                    className="pb-cat flex items-center justify-center gap-2 py-1"
                    style={{ gridColumn: '1 / -1' }}
                >
                    <button type="button" className="pb-btn" onClick={onCancel}>
                        Cancel
                    </button>
                    <button
                        type="submit"
                        className="pb-btn pb-btn-primary"
                        disabled={!canSubmit}
                    >
                        {create.isPending ? 'Posting…' : 'Submit'}
                    </button>
                </div>
            </div>
            {create.isError && (
                <div className="pb-small" style={{ color: '#aa0000' }}>
                    Could not post: {(create.error as Error).message}
                </div>
            )}
        </form>
    );
}
