import { useMemo } from 'react';
import type { ImportLedger } from '../../../api/model';
import { dispositionColor, num } from '../lib/workshop-utils';

const DISPOSITION_ORDER = [
    'event-routed',
    'folded',
    'history-only',
    'discarded',
    'unprocessed',
];

/// The conservation ledger: every chunk accounted for, no silent loss ever. Session
/// totals plus the per-chunk dispositions, filterable to the selected scene.
export default function LedgerPanel({
    ledger,
    selectedSceneId,
}: {
    ledger: ImportLedger;
    selectedSceneId: string | null;
}) {
    const chunks = useMemo(() => {
        const all = ledger.chunks ?? [];
        return selectedSceneId
            ? all.filter((c) => c.sceneId === selectedSceneId)
            : all;
    }, [ledger.chunks, selectedSceneId]);

    const counts = useMemo(() => {
        const map = new Map<string, number>();
        for (const chunk of chunks) {
            const key = chunk.disposition ?? 'unprocessed';
            map.set(key, (map.get(key) ?? 0) + 1);
        }
        return map;
    }, [chunks]);

    return (
        <div className="flex flex-col gap-2">
            <div className="flex flex-wrap items-center gap-1">
                <span className="font-semibold">
                    {selectedSceneId ? 'Scene ledger' : 'Session ledger'}
                </span>
                <span className="xp-glass-chip">
                    {chunks.length}
                    {selectedSceneId
                        ? ' chunks in scene'
                        : ` / ${num(ledger.totalChunks)} chunks`}
                </span>
                {!selectedSceneId ? (
                    <span
                        className="xp-glass-chip"
                        style={{
                            color: ledger.reconciles ? '#2e8b57' : '#c00',
                            fontWeight: 700,
                        }}
                        title="Conservation invariant: every chunk is accounted for"
                    >
                        {ledger.reconciles
                            ? '✔ reconciles'
                            : '✘ does not reconcile'}
                    </span>
                ) : null}
            </div>
            <div className="flex flex-wrap gap-1">
                {DISPOSITION_ORDER.map((disposition) => (
                    <span
                        key={disposition}
                        className="xp-glass-chip flex items-center gap-1"
                    >
                        <span
                            className="inline-block h-[8px] w-[8px] rounded-full"
                            style={{
                                background: dispositionColor(disposition),
                            }}
                        />
                        {disposition}: {counts.get(disposition) ?? 0}
                    </span>
                ))}
            </div>
            <table className="w-full border-collapse text-left">
                <thead>
                    <tr style={{ color: '#555' }}>
                        <th className="pr-2">#</th>
                        <th className="pr-2">category</th>
                        <th className="pr-2">disposition</th>
                        <th>reason</th>
                    </tr>
                </thead>
                <tbody>
                    {chunks.map((chunk) => (
                        <tr
                            key={num(chunk.chunkIndex)}
                            style={{
                                borderTop: '1px solid rgba(0,0,0,0.06)',
                            }}
                        >
                            <td className="pr-2" style={{ color: '#666' }}>
                                {num(chunk.chunkIndex)}
                            </td>
                            <td className="pr-2">{chunk.category}</td>
                            <td className="pr-2">
                                <span
                                    className="mr-1 inline-block h-[8px] w-[8px] rounded-full"
                                    style={{
                                        background: dispositionColor(
                                            chunk.disposition,
                                        ),
                                    }}
                                />
                                {chunk.disposition}
                            </td>
                            <td style={{ color: '#555' }}>
                                {chunk.reason ?? ''}
                            </td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}
