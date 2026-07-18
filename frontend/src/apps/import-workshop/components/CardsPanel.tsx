import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { usePutImportIdCardsName } from '#api/party-zone';
import type { ImportDraftView, PersonaCardDraft } from '../../../api/model';
import { invalidateImportSession } from '../lib/workshop-utils';

/// Persona card review — the human gate the ADR requires: every finalize output is
/// edited or blessed here before commit will mint or update a persona.
export default function CardsPanel({
    sessionId,
    draft,
}: {
    sessionId: string;
    draft: ImportDraftView;
}) {
    const cards = draft.cards ?? [];
    if (cards.length === 0) {
        return (
            <p style={{ color: '#555' }}>
                No cards yet. Run a scene, then Finalize it — each touched
                persona gets a card (system prompt + one-line bio) to review
                here.
            </p>
        );
    }
    return (
        <div className="flex flex-col gap-2">
            {cards.map((card) => (
                <CardEditor
                    key={card.persona}
                    sessionId={sessionId}
                    card={card}
                />
            ))}
        </div>
    );
}

function CardEditor({
    sessionId,
    card,
}: {
    sessionId: string;
    card: PersonaCardDraft;
}) {
    const queryClient = useQueryClient();
    const [systemPrompt, setSystemPrompt] = useState(card.systemPrompt ?? '');
    const [bio, setBio] = useState(card.bio ?? '');
    const [dirty, setDirty] = useState(false);

    const updateCard = usePutImportIdCardsName({
        mutation: {
            onSuccess: () => {
                setDirty(false);
                invalidateImportSession(queryClient, sessionId);
            },
        },
    });

    const name = card.persona ?? '';
    const hasProposal =
        card.proposedSystemPrompt != null || card.proposedBio != null;
    const committedDrift =
        card.committedSystemPrompt != null &&
        (card.committedSystemPrompt !== card.systemPrompt ||
            (card.committedBio ?? '') !== (card.bio ?? ''));

    return (
        <div className="xp-glass-card flex flex-col gap-1 p-2">
            <div className="flex flex-wrap items-center gap-1">
                <span className="font-semibold">{name}</span>
                {card.humanEdited ? (
                    <span
                        className="xp-glass-chip"
                        title="Your edit wins: later finalizes only propose, never overwrite"
                    >
                        ✍ human-edited
                    </span>
                ) : null}
                {card.committedSystemPrompt != null ? (
                    <span
                        className="xp-glass-chip"
                        style={{
                            color: committedDrift ? '#b8860b' : '#2e8b57',
                        }}
                        title={
                            committedDrift
                                ? 'Card changed since the last commit — commit again to apply'
                                : 'Card matches what was committed'
                        }
                    >
                        {committedDrift
                            ? '● changed since commit'
                            : '✔ committed'}
                    </span>
                ) : null}
            </div>

            <label className="flex flex-col gap-[2px]">
                <span style={{ color: '#555' }}>bio (one line)</span>
                <input
                    type="text"
                    value={bio}
                    onChange={(e) => {
                        setBio(e.target.value);
                        setDirty(true);
                    }}
                />
            </label>
            <label className="flex flex-col gap-[2px]">
                <span style={{ color: '#555' }}>system prompt</span>
                <textarea
                    rows={5}
                    value={systemPrompt}
                    onChange={(e) => {
                        setSystemPrompt(e.target.value);
                        setDirty(true);
                    }}
                />
            </label>
            <div className="flex gap-1">
                <button
                    type="button"
                    className="xp-glass-chip cursor-pointer font-semibold"
                    disabled={!dirty || updateCard.isPending}
                    onClick={() =>
                        updateCard.mutate({
                            id: sessionId,
                            name,
                            data: { systemPrompt, bio: bio || null },
                        })
                    }
                >
                    {updateCard.isPending ? 'Saving…' : 'Save edit'}
                </button>
                {dirty ? (
                    <button
                        type="button"
                        className="xp-glass-chip cursor-pointer"
                        onClick={() => {
                            setSystemPrompt(card.systemPrompt ?? '');
                            setBio(card.bio ?? '');
                            setDirty(false);
                        }}
                    >
                        Revert
                    </button>
                ) : null}
            </div>

            {hasProposal ? (
                <div
                    className="flex flex-col gap-1 p-1"
                    style={{
                        background: 'rgba(184,134,11,0.08)',
                        border: '1px solid rgba(184,134,11,0.4)',
                        borderRadius: '3px',
                    }}
                >
                    <span
                        className="font-semibold"
                        style={{ color: '#b8860b' }}
                    >
                        Re-finalize proposal (your edit was preserved)
                    </span>
                    {card.proposedBio != null ? (
                        <span>
                            <b>bio:</b> {card.proposedBio}
                        </span>
                    ) : null}
                    {card.proposedSystemPrompt != null ? (
                        <pre
                            className="whitespace-pre-wrap"
                            style={{ margin: 0, fontFamily: 'inherit' }}
                        >
                            {card.proposedSystemPrompt}
                        </pre>
                    ) : null}
                    <div>
                        <button
                            type="button"
                            className="xp-glass-chip cursor-pointer"
                            disabled={updateCard.isPending}
                            onClick={() =>
                                updateCard.mutate({
                                    id: sessionId,
                                    name,
                                    data: { acceptProposal: true },
                                })
                            }
                        >
                            Accept proposal
                        </button>
                    </div>
                </div>
            ) : null}
            {updateCard.isError ? (
                <span style={{ color: '#c00' }}>
                    {(updateCard.error as Error).message}
                </span>
            ) : null}
        </div>
    );
}
