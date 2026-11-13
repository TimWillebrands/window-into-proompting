import { type FormEvent, useId, useState } from 'react';
import {
    postWorldsPartyIdMove,
    useGetWorldsPartyIdLives,
    useGetWorldsPartyIdRelations,
} from '#api/party-zone';
import { ProblemDetailsError } from '../../api/custom-fetch';
import type { Relation, WorldTile } from '../../api/model';
import { inOrder, summary } from '../../apps/relations/relations-lib';

const MAX_NAME = 60;

interface MoveWorldDialogProps {
    from: WorldTile;
    onCancel: () => void;
    onMoved: (to: WorldTile) => void;
}

/** Admin: everyone ticked wakes up in a new World with their own nature and the Relations ticked; this one stops. */
export default function MoveWorldDialog({
    from,
    onCancel,
    onMoved,
}: MoveWorldDialogProps) {
    const formId = useId();
    const lives = useGetWorldsPartyIdLives(from.id).data?.data ?? [];
    const relations = inOrder(
        useGetWorldsPartyIdRelations(from.id).data?.data ?? [],
    );
    const [name, setName] = useState('');
    const [left, setLeft] = useState<Set<string>>(new Set());
    const [kept, setKept] = useState<Set<string>>(new Set());
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const nameOf = (id?: string) =>
        lives.find((l) => l.personaId === id)?.name ?? 'someone gone';
    const coming = (r: Relation) =>
        !left.has(r.aId ?? '') && !left.has(r.bId ?? '');
    const toggle = (set: Set<string>, id: string) => {
        const next = new Set(set);
        if (!next.delete(id)) next.add(id);
        return next;
    };

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        setBusy(true);
        setError(null);
        try {
            const res = await postWorldsPartyIdMove(from.id, {
                name: name.trim(),
                participantIds:
                    left.size === 0
                        ? null
                        : lives
                              .filter((l) => !left.has(l.personaId))
                              .map((l) => l.personaId),
                relationIds: relations
                    .filter((r) => r.id && kept.has(r.id) && coming(r))
                    .map((r) => r.id as string),
            });
            if (res.status === 200) onMoved(res.data);
        } catch (err) {
            const said =
                err instanceof ProblemDetailsError
                    ? err.detail || err.title
                    : null;
            setError(said || 'That did not work. Try again?');
            setBusy(false);
        }
    };

    return (
        <div className="window ml-16 max-w-md text-black">
            <div className="title-bar">
                <div className="title-bar-text">
                    Move everyone out of {from.name}
                </div>
                <div className="title-bar-controls">
                    <button
                        type="button"
                        aria-label="Close"
                        onClick={onCancel}
                    />
                </div>
            </div>
            <form className="window-body flex flex-col gap-2" onSubmit={submit}>
                <p className="m-0">
                    Everyone ticked wakes up in a new World as who they are,
                    remembering nothing of {from.name} but the Relations ticked.{' '}
                    {from.name} stops and keeps everything; the new World
                    becomes current.
                </p>
                <div className="field-row-stacked">
                    <label htmlFor={`${formId}-name`}>New World's name</label>
                    <input
                        id={`${formId}-name`}
                        value={name}
                        maxLength={MAX_NAME}
                        onChange={(e) => setName(e.target.value)}
                        autoComplete="off"
                    />
                </div>
                <fieldset>
                    <legend>Who comes along</legend>
                    {lives.map((l) => (
                        <div key={l.personaId} className="field-row">
                            <input
                                id={`${formId}-p-${l.personaId}`}
                                type="checkbox"
                                checked={!left.has(l.personaId)}
                                onChange={() =>
                                    setLeft((s) => toggle(s, l.personaId))
                                }
                            />
                            <label htmlFor={`${formId}-p-${l.personaId}`}>
                                {l.name}
                                {l.driver === 'User' ? ' (human)' : ''}
                            </label>
                        </div>
                    ))}
                </fieldset>
                <fieldset>
                    <legend>Relations that come along, story and all</legend>
                    {relations.length === 0 && <p className="m-0">None.</p>}
                    {relations.map((r) => (
                        <div key={r.id} className="field-row">
                            <input
                                id={`${formId}-r-${r.id}`}
                                type="checkbox"
                                disabled={!coming(r)}
                                checked={!!r.id && kept.has(r.id) && coming(r)}
                                onChange={() =>
                                    r.id &&
                                    setKept((s) => toggle(s, r.id as string))
                                }
                            />
                            <label htmlFor={`${formId}-r-${r.id}`}>
                                {nameOf(r.aId)} &amp; {nameOf(r.bId)}:{' '}
                                {summary(r)}
                            </label>
                        </div>
                    ))}
                </fieldset>
                {error && (
                    <p role="alert" className="m-0">
                        {error}
                    </p>
                )}
                <div className="field-row justify-end">
                    <button type="button" onClick={onCancel}>
                        Cancel
                    </button>
                    <button type="submit" disabled={busy || !name.trim()}>
                        {busy ? 'Moving…' : 'Move'}
                    </button>
                </div>
            </form>
        </div>
    );
}
