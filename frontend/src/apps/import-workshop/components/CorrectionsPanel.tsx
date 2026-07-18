import { useGetImportIdCorrectionsSuspense } from '#api/party-zone';
import type { ImportCorrection } from '../../../api/model';

/// The durable correction ledger: at each commit, the diff between what the extractor
/// suggested and what the human shipped. Outlives the session; feeds the bench.
export default function CorrectionsPanel({ sessionId }: { sessionId: string }) {
    const corrections = (useGetImportIdCorrectionsSuspense(sessionId).data
        .data ?? []) as ImportCorrection[];

    if (corrections.length === 0) {
        return (
            <p style={{ color: '#555' }}>
                No corrections recorded yet — they are written at scene commit,
                one entry per human change (promoted, demoted, reweighted,
                renamed, match-flipped, …).
            </p>
        );
    }
    return (
        <div className="flex flex-col gap-1">
            {corrections.map((correction) => (
                <div
                    key={correction.id}
                    className="xp-glass-card flex flex-col gap-[2px] p-2"
                >
                    <div className="flex items-center gap-1">
                        <span className="xp-glass-chip font-semibold">
                            {correction.kind}
                        </span>
                        <span style={{ color: '#888' }}>
                            chunks{' '}
                            {(correction.chunkRefs ?? [])
                                .map((c) => Number(c))
                                .join(', ')}
                        </span>
                        <span className="flex-1" />
                        <span style={{ color: '#888' }}>
                            {correction.committedAt
                                ? new Date(
                                      correction.committedAt,
                                  ).toLocaleString()
                                : ''}
                        </span>
                    </div>
                    {correction.suggested ? (
                        <span style={{ color: '#a05a2c' }}>
                            suggested: {correction.suggested}
                        </span>
                    ) : null}
                    {correction.final ? (
                        <span style={{ color: '#2e8b57' }}>
                            final: {correction.final}
                        </span>
                    ) : null}
                    {correction.note ? (
                        <span style={{ color: '#555' }}>
                            note: “{correction.note}”
                        </span>
                    ) : null}
                </div>
            ))}
        </div>
    );
}
