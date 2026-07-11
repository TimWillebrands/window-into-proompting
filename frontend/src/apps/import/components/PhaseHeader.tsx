import type { CSSProperties } from 'react';
import {
    type ImportPhase,
    type PhaseStatus,
    selectIncludedCount,
    useImportStore,
} from '../state/import-store';

type Props = {
    phase: ImportPhase;
    onIdentify: () => void;
    onClassify: () => void;
    onCommit: () => void;
    canIdentify: boolean;
    canClassify: boolean;
    canCommit: boolean;
    onClearDraft: () => void;
};

const phaseLabel: Record<PhaseStatus, string> = {
    idle: 'idle',
    running: 'running',
    done: 'done',
    stale: 'stale',
};

function statusFor(s: PhaseStatus, hasAny: boolean): PhaseStatus {
    if (s === 'idle' && hasAny) return 'done';
    return s;
}

export function PhaseHeader({
    phase,
    onIdentify,
    onClassify,
    onCommit,
    canIdentify,
    canClassify,
    canCommit,
    onClearDraft,
}: Props) {
    const includedCount = useImportStore(selectIncludedCount);
    const charCount = useImportStore((s) => s.roster.charOrder.length);
    const segCount = useImportStore((s) => {
        let n = 0;
        for (const id of s.msgOrder) n += s.msgs[id]?.segments.length ?? 0;
        return n;
    });
    const fileName = useImportStore((s) => s.file?.fileName ?? '');
    const staleReason = phase.staleReason;

    const idStatus = statusFor(phase.identify, charCount > 0);
    const clStatus = statusFor(phase.classify, segCount > 0);

    return (
        <header className="imp-phase-bar">
            <button
                type="button"
                className="imp-phase-btn"
                data-status="cta"
                onClick={onClearDraft}
                title="Pick a different file or clear the saved draft."
            >
                <span className="imp-phase-step">01</span>
                <span>Import</span>
                <span style={dotStyle('idle', fileName ? 'done' : 'idle')} />
            </button>
            <button
                type="button"
                className="imp-phase-btn"
                data-status={idStatus}
                disabled={!canIdentify}
                onClick={onIdentify}
                title={staleReason ?? 'Run the per-message identify pipeline.'}
            >
                <span className="imp-phase-step">02</span>
                <span>Identify Characters</span>
                <span
                    className="imp-phase-dot"
                    data-status={idStatus}
                    style={dotStyle(idStatus, 'idle')}
                />
            </button>
            <button
                type="button"
                className="imp-phase-btn"
                data-status={clStatus}
                disabled={!canClassify}
                onClick={onClassify}
                title="Split each message into per-character segments."
            >
                <span className="imp-phase-step">03</span>
                <span>Identify Message Parts</span>
                <span
                    className="imp-phase-dot"
                    data-status={clStatus}
                    style={dotStyle(clStatus, 'idle')}
                />
            </button>
            <button
                type="button"
                className="imp-phase-btn"
                data-status="cta"
                disabled={!canCommit}
                onClick={onCommit}
                title="Open the create-room popover."
            >
                <span className="imp-phase-step">04</span>
                <span>Create Room</span>
            </button>
            <span className="imp-phase-tail">
                <span>{includedCount} msgs</span>
                <span>·</span>
                <span>{charCount} chars</span>
                <span>·</span>
                <span>{segCount} segments</span>
                <span>·</span>
                <span
                    title={fileName}
                    style={{
                        maxWidth: 220,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                    }}
                >
                    {fileName}
                </span>
                <span>·</span>
                <span
                    title="Status of the two pipeline runs — stale means the selection changed since the last run"
                    style={{
                        color:
                            idStatus === 'stale' || clStatus === 'stale'
                                ? 'rgb(255, 200, 160)'
                                : undefined,
                    }}
                >
                    characters {phaseLabel[idStatus]} · parts{' '}
                    {phaseLabel[clStatus]}
                </span>
            </span>
        </header>
    );
}

function dotStyle(_a: PhaseStatus, _b: PhaseStatus): CSSProperties {
    return {};
}
