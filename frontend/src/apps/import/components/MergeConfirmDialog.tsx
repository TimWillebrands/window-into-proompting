import { useState } from 'react';
import { type ExtractedPersona, mergePersonas } from '../../../lib/import-api';
import { type CharId, useImportStore } from '../state/import-store';

type Props = {
    survivorId: CharId;
    victimId: CharId;
    onClose: () => void;
};

export function MergeConfirmDialog({ survivorId, victimId, onClose }: Props) {
    const survivor = useImportStore((s) => s.roster.chars[survivorId]);
    const victim = useImportStore((s) => s.roster.chars[victimId]);
    const mergeLocal = useImportStore((s) => s.mergeCharsLocal);
    const onIdentifyReduceProgress = useImportStore(
        (s) => s.onIdentifyReduceProgress,
    );
    const setField = useImportStore((s) => s.setCharField);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    if (!survivor || !victim) return null;

    const onConfirm = async () => {
        setBusy(true);
        setError(null);
        try {
            const stubs: ExtractedPersona[] = [
                {
                    name: survivor.primaryName,
                    archetype: survivor.archetype,
                    system_prompt: survivor.prompt,
                    bio: survivor.bio,
                },
                {
                    name: victim.primaryName,
                    archetype: victim.archetype,
                    system_prompt: victim.prompt,
                    bio: victim.bio,
                },
            ];
            const merged = await mergePersonas(stubs);
            mergeLocal(survivorId, victimId);
            onIdentifyReduceProgress(
                survivorId,
                merged.system_prompt,
                merged.bio ?? null,
            );
            if (merged.name && merged.name !== survivor.primaryName) {
                setField(survivorId, 'primaryName', merged.name);
            }
            if (merged.archetype) {
                setField(survivorId, 'archetype', merged.archetype);
            }
            onClose();
        } catch (e) {
            setError(e instanceof Error ? e.message : String(e));
        } finally {
            setBusy(false);
        }
    };

    return (
        <div
            style={{
                position: 'absolute',
                inset: 0,
                background: 'rgba(0,0,0,0.45)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                zIndex: 70,
            }}
        >
            <div
                style={{
                    background: 'rgba(15, 24, 44, 0.97)',
                    border: '1px solid rgba(150, 195, 255, 0.45)',
                    borderRadius: 8,
                    padding: 16,
                    width: 380,
                    color: 'rgb(232 240 252)',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 10,
                }}
            >
                <h3 style={{ margin: 0, fontFamily: 'Georgia, serif' }}>
                    Merge characters?
                </h3>
                <p
                    style={{
                        margin: 0,
                        fontSize: 11,
                        color: 'rgba(220,230,250,0.85)',
                        lineHeight: 1.4,
                    }}
                >
                    Merging will collapse{' '}
                    <strong style={{ color: victim.color }}>
                        {victim.primaryName}
                    </strong>{' '}
                    into{' '}
                    <strong style={{ color: survivor.color }}>
                        {survivor.primaryName}
                    </strong>
                    . All mentions and segments pointing at the first character
                    will be re-assigned to the second. The LLM is asked to
                    synthesise a single merged prompt + bio from both inputs.
                </p>
                {error && (
                    <div
                        style={{
                            background: 'rgba(240,130,114,0.18)',
                            padding: 6,
                            fontSize: 11,
                            borderRadius: 3,
                        }}
                    >
                        {error}
                    </div>
                )}
                <div
                    style={{
                        display: 'flex',
                        gap: 6,
                        justifyContent: 'flex-end',
                        marginTop: 6,
                    }}
                >
                    <button
                        type="button"
                        className="imp-detail-btn"
                        onClick={onClose}
                        disabled={busy}
                    >
                        Cancel
                    </button>
                    <button
                        type="button"
                        className="imp-detail-btn"
                        data-tone="danger"
                        onClick={onConfirm}
                        disabled={busy}
                    >
                        {busy ? 'Merging…' : 'Merge via LLM'}
                    </button>
                </div>
            </div>
        </div>
    );
}
