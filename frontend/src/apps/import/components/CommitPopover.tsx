import { useState } from 'react';
import { useImportStore } from '../state/import-store';

type Props = {
    onCommit: () => Promise<void>;
    onClose: () => void;
    busy: boolean;
};

export function CommitPopover({ onCommit, onClose, busy }: Props) {
    const metadata = useImportStore((s) => s.metadata);
    const setMetadata = useImportStore((s) => s.setMetadata);
    const charCount = useImportStore((s) => s.roster.charOrder.length);
    const segCount = useImportStore((s) => {
        let n = 0;
        for (const id of s.msgOrder) n += s.msgs[id]?.segments.length ?? 0;
        return n;
    });
    const [error, setError] = useState<string | null>(null);

    const submit = async () => {
        setError(null);
        try {
            await onCommit();
        } catch (e) {
            setError(e instanceof Error ? e.message : String(e));
        }
    };

    return (
        <div className="imp-commit-pop">
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
                <h3>Create chat room</h3>
                <span
                    style={{
                        marginLeft: 'auto',
                        fontFamily: 'Consolas, monospace',
                        fontSize: 10,
                        color: 'rgba(180,200,230,0.65)',
                    }}
                >
                    {charCount} chars · {segCount} segments
                </span>
            </div>
            <label>
                <span>Room title</span>
                <input
                    className="imp-detail-input"
                    value={metadata.title}
                    maxLength={120}
                    onChange={(e) =>
                        setMetadata({ title: e.currentTarget.value })
                    }
                />
            </label>
            <label>
                <span>Scenario (optional)</span>
                <textarea
                    className="imp-detail-textarea"
                    rows={2}
                    value={metadata.scenario}
                    maxLength={2000}
                    onChange={(e) =>
                        setMetadata({ scenario: e.currentTarget.value })
                    }
                />
            </label>
            <div
                style={{
                    display: 'grid',
                    gridTemplateColumns: '1fr 80px',
                    gap: 6,
                }}
            >
                <label>
                    <span>Base time</span>
                    <input
                        type="datetime-local"
                        className="imp-detail-input"
                        value={metadata.baseDateIso}
                        onChange={(e) =>
                            setMetadata({ baseDateIso: e.currentTarget.value })
                        }
                    />
                </label>
                <label>
                    <span>Step (s)</span>
                    <input
                        type="number"
                        min={1}
                        className="imp-detail-input"
                        value={metadata.stepSeconds}
                        onChange={(e) =>
                            setMetadata({
                                stepSeconds:
                                    Number.parseInt(
                                        e.currentTarget.value,
                                        10,
                                    ) || 60,
                            })
                        }
                    />
                </label>
            </div>
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
                    marginTop: 4,
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
                    onClick={submit}
                    disabled={busy || charCount === 0 || segCount === 0}
                    style={{
                        background:
                            'linear-gradient(180deg, rgb(80 150 240), rgb(40 90 200))',
                        color: 'white',
                        borderColor: 'rgba(255,255,255,0.4)',
                    }}
                >
                    {busy ? 'Creating…' : 'Create room →'}
                </button>
            </div>
        </div>
    );
}
