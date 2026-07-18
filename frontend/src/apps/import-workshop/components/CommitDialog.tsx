import { Suspense, useState } from 'react';
import { postPartyCreate, useGetPartySuspense } from '#api/party-zone';
import type { PartyInfo } from '../../../api/model';

/// First-commit targeting: the session pins its Room here; every later commit extends
/// the same Room without asking again.
export default function CommitDialog({
    busy,
    onClose,
    onSubmit,
}: {
    busy: boolean;
    onClose: () => void;
    onSubmit: (target: { partyId: string; roomName?: string }) => void;
}) {
    return (
        <div
            className="absolute inset-0 z-10 flex items-center justify-center"
            style={{ background: 'rgba(0,0,0,0.25)' }}
        >
            <div className="xp-glass-panel flex w-[320px] flex-col gap-2 p-3">
                <span className="font-semibold">
                    Where should this import live?
                </span>
                <Suspense fallback={<progress />}>
                    <CommitDialogBody
                        busy={busy}
                        onClose={onClose}
                        onSubmit={onSubmit}
                    />
                </Suspense>
            </div>
        </div>
    );
}

function CommitDialogBody({
    busy,
    onClose,
    onSubmit,
}: {
    busy: boolean;
    onClose: () => void;
    onSubmit: (target: { partyId: string; roomName?: string }) => void;
}) {
    const parties = (useGetPartySuspense().data.data ?? []) as PartyInfo[];
    const [partyId, setPartyId] = useState<string>(parties[0]?.id ?? 'new');
    const [newPartyName, setNewPartyName] = useState('Imported party');
    const [roomName, setRoomName] = useState('');
    const [creating, setCreating] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const submit = async () => {
        setError(null);
        try {
            let targetPartyId = partyId;
            if (partyId === 'new') {
                setCreating(true);
                const created = await postPartyCreate({
                    partyName: newPartyName.trim() || 'Imported party',
                });
                const info = created.data as PartyInfo;
                if (!info?.id) throw new Error('party create returned no id');
                targetPartyId = info.id;
            }
            onSubmit({
                partyId: targetPartyId,
                roomName: roomName.trim() || undefined,
            });
        } catch (e) {
            setError(e instanceof Error ? e.message : String(e));
        } finally {
            setCreating(false);
        }
    };

    return (
        <>
            <label className="flex flex-col gap-[2px]">
                Party
                <select
                    value={partyId}
                    onChange={(e) => setPartyId(e.target.value)}
                >
                    {parties.map((party) => (
                        <option key={party.id} value={party.id}>
                            {party.name ?? party.id}
                        </option>
                    ))}
                    <option value="new">＋ New party…</option>
                </select>
            </label>
            {partyId === 'new' ? (
                <label className="flex flex-col gap-[2px]">
                    New party name
                    <input
                        type="text"
                        value={newPartyName}
                        onChange={(e) => setNewPartyName(e.target.value)}
                    />
                </label>
            ) : null}
            <label className="flex flex-col gap-[2px]">
                Room name (optional)
                <input
                    type="text"
                    value={roomName}
                    placeholder="defaults to the export file name"
                    onChange={(e) => setRoomName(e.target.value)}
                />
            </label>
            {error ? <span style={{ color: '#c00' }}>{error}</span> : null}
            <div className="flex justify-end gap-1">
                <button
                    type="button"
                    className="xp-glass-chip cursor-pointer"
                    onClick={onClose}
                >
                    Cancel
                </button>
                <button
                    type="button"
                    className="xp-glass-chip cursor-pointer font-semibold"
                    disabled={busy || creating}
                    onClick={submit}
                >
                    {creating || busy ? 'Committing…' : 'Commit'}
                </button>
            </div>
        </>
    );
}
