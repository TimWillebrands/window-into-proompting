import { type ReactNode, useId, useState } from 'react';
import {
    postWorldsPartyIdMove,
    useGetWorldsPartyIdLives,
    useGetWorldsPartyIdMovablePlaces,
    useGetWorldsPartyIdRelations,
} from '#api/party-zone';
import { ProblemDetailsError } from '../../api/custom-fetch';
import type { MovablePlace, Relation, WorldTile } from '../../api/model';
import { PlaceOrigin } from '../../api/model';
import { inOrder, summary } from '../../apps/relations/relations-lib';

const MAX_NAME = 60;
const STEPS = ['Name', 'People', 'Relations', 'Places', 'Ready'] as const;

interface MoveWorldDialogProps {
    from: WorldTile;
    onCancel: () => void;
    onMoved: (to: WorldTile) => void;
}

const toggled = (set: Set<string>, id: string) => {
    const next = new Set(set);
    if (!next.delete(id)) next.add(id);
    return next;
};

/** Where a Place came from, as the wizard says it. */
function origin(p: MovablePlace): string {
    switch (p.origin) {
        case PlaceOrigin.Author:
            return 'made by an admin';
        case PlaceOrigin.Player:
            return `made by ${p.by ?? 'a Player'}`;
        case PlaceOrigin.Persona:
            return `made by ${p.by ?? 'a persona'}, a persona`;
        case PlaceOrigin.Opened:
            return 'opened as the town grew';
        default:
            return `${p.by ?? 'a Player'}'s Home`;
    }
}

interface CheckRowProps {
    id: string;
    checked: boolean;
    disabled?: boolean;
    onToggle: () => void;
    children: ReactNode;
}

function CheckRow({
    id,
    checked,
    disabled,
    onToggle,
    children,
}: CheckRowProps) {
    return (
        <div className="field-row">
            <input
                id={id}
                type="checkbox"
                checked={checked}
                disabled={disabled}
                onChange={onToggle}
            />
            <label htmlFor={id}>{children}</label>
        </div>
    );
}

/** Admin wizard: who, which Relations and which Places wake up in a new World; this one stops. */
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
    const movable = useGetWorldsPartyIdMovablePlaces(from.id).data?.data ?? [];
    const [step, setStep] = useState(0);
    const [name, setName] = useState('');
    const [left, setLeft] = useState<Set<string>>(new Set());
    const [kept, setKept] = useState<Set<string>>(new Set());
    /** Places whose tick differs from the suggestion. */
    const [flipped, setFlipped] = useState<Set<string>>(new Set());
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const nameOf = (id?: string | null) =>
        lives.find((l) => l.personaId === id)?.name ?? 'someone gone';
    const comes = (id?: string | null) => !id || !left.has(id);
    const relationComes = (r: Relation) =>
        !!r.id && kept.has(r.id) && comes(r.aId) && comes(r.bId);
    const homeless = (p: MovablePlace) =>
        p.origin === PlaceOrigin.Home && !comes(p.byId);
    const placeComes = (p: MovablePlace) =>
        !homeless(p) && p.suggested !== flipped.has(p.id);

    const people = lives.filter((l) => comes(l.personaId));
    const carriedRelations = relations.filter(relationComes);
    const carriedPlaces = movable.filter(placeComes);

    const move = async () => {
        setBusy(true);
        setError(null);
        try {
            const res = await postWorldsPartyIdMove(from.id, {
                name: name.trim(),
                participantIds:
                    left.size === 0 ? null : people.map((l) => l.personaId),
                relationIds: carriedRelations.map((r) => r.id as string),
                placeIds: carriedPlaces.map((p) => p.id),
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

    const pages = [
        <>
            <p className="m-0">
                Everyone you pick wakes up in a new World as who they are,
                remembering nothing of {from.name} but the Relations you pick.{' '}
                {from.name} stops and keeps everything; the new World becomes
                current.
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
        </>,
        <fieldset key="people">
            <legend>Who comes along</legend>
            {lives.map((l) => (
                <CheckRow
                    key={l.personaId}
                    id={`${formId}-p-${l.personaId}`}
                    checked={comes(l.personaId)}
                    onToggle={() => setLeft((s) => toggled(s, l.personaId))}
                >
                    {l.name}
                    {l.driver === 'User' ? ' (human)' : ''}
                </CheckRow>
            ))}
        </fieldset>,
        <fieldset key="relations">
            <legend>Relations that come along, story and all</legend>
            {relations.length === 0 && <p className="m-0">None.</p>}
            {relations.map((r) => (
                <CheckRow
                    key={r.id}
                    id={`${formId}-r-${r.id}`}
                    disabled={!comes(r.aId) || !comes(r.bId)}
                    checked={relationComes(r)}
                    onToggle={() =>
                        r.id && setKept((s) => toggled(s, r.id as string))
                    }
                >
                    {nameOf(r.aId)} &amp; {nameOf(r.bId)}: {summary(r)}
                </CheckRow>
            ))}
        </fieldset>,
        <fieldset key="places">
            <legend>Places that come along, as they were made</legend>
            <p className="m-0">
                The usual town and a fresh Home for every persona are there
                anyway. People, keys and guests stay behind.
            </p>
            {movable.length === 0 && <p className="m-0">Nothing made.</p>}
            {movable.map((p) => (
                <CheckRow
                    key={p.id}
                    id={`${formId}-pl-${p.id}`}
                    disabled={homeless(p)}
                    checked={placeComes(p)}
                    onToggle={() => setFlipped((s) => toggled(s, p.id))}
                >
                    {p.name} <span className="opacity-70">({origin(p)})</span>
                </CheckRow>
            ))}
        </fieldset>,
        <>
            <p className="m-0">Ready to move to {name.trim()}:</p>
            <ul className="m-0">
                <li>
                    {people.length} of {lives.length} people
                </li>
                <li>
                    {carriedRelations.length} of {relations.length} Relations
                </li>
                <li>
                    {carriedPlaces.length} of {movable.length} made Places
                </li>
            </ul>
            <p className="m-0">
                {from.name} stops. Everything else they had there stays behind
                with it.
            </p>
        </>,
    ];
    const last = step === STEPS.length - 1;

    return (
        <div className="window ml-16 max-w-md text-black">
            <div className="title-bar">
                <div className="title-bar-text">Move Wizard: {from.name}</div>
                <div className="title-bar-controls">
                    <button
                        type="button"
                        aria-label="Close"
                        onClick={onCancel}
                    />
                </div>
            </div>
            <div className="window-body flex flex-col gap-2">
                <p className="m-0 font-bold">
                    Step {step + 1} of {STEPS.length}: {STEPS[step]}
                </p>
                {pages[step]}
                {error && (
                    <p role="alert" className="m-0">
                        {error}
                    </p>
                )}
                <div className="field-row justify-end">
                    <button
                        type="button"
                        disabled={step === 0 || busy}
                        onClick={() => setStep((s) => s - 1)}
                    >
                        &lt; Back
                    </button>
                    {last ? (
                        <button type="button" disabled={busy} onClick={move}>
                            {busy ? 'Moving…' : 'Move'}
                        </button>
                    ) : (
                        <button
                            type="button"
                            disabled={!name.trim()}
                            onClick={() => setStep((s) => s + 1)}
                        >
                            Next &gt;
                        </button>
                    )}
                    <button type="button" disabled={busy} onClick={onCancel}>
                        Cancel
                    </button>
                </div>
            </div>
        </div>
    );
}
