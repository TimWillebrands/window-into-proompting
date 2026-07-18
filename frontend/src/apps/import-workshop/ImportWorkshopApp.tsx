import { useCallback, useState } from 'react';
import { useGetImportId, usePostImport } from '#api/party-zone';
import type { ImportSessionOverview } from '../../api/model';
import { loadStoredSessionId, storeSessionId } from './lib/workshop-utils';
import WorkshopScreen from './WorkshopScreen';

/// Import Workshop (ADR 0017): upload a Gemini export, carve it into scenes, run the
/// extraction map, review/correct the draft, commit scene by scene into a real Room.
export default function ImportWorkshopApp() {
    const [sessionId, setSessionId] = useState<string | null>(() =>
        loadStoredSessionId(),
    );

    const adoptSession = useCallback((id: string | null) => {
        storeSessionId(id);
        setSessionId(id);
    }, []);

    if (!sessionId) {
        return <UploadScreen onCreated={adoptSession} />;
    }
    return (
        <SessionGate
            key={sessionId}
            sessionId={sessionId}
            onExit={() => adoptSession(null)}
        />
    );
}

/// Confirms the stored session still exists server-side before mounting the workshop —
/// uncommitted work survives refresh because all state lives in the session grain.
function SessionGate({
    sessionId,
    onExit,
}: {
    sessionId: string;
    onExit: () => void;
}) {
    const overviewQuery = useGetImportId(sessionId, {
        query: { retry: false },
    });

    if (overviewQuery.isPending) {
        return (
            <div className="app-surface flex h-full items-center justify-center">
                <progress />
            </div>
        );
    }
    if (overviewQuery.isError) {
        return (
            <div className="app-surface flex h-full flex-col items-center justify-center gap-2 p-4 text-[11px]">
                <p>
                    The stored import session could not be loaded — it may have
                    been abandoned or the backend restarted without it.
                </p>
                <button
                    type="button"
                    className="xp-glass-chip cursor-pointer"
                    onClick={onExit}
                >
                    Start a new import
                </button>
            </div>
        );
    }
    return <WorkshopScreen sessionId={sessionId} onExit={onExit} />;
}

function UploadScreen({ onCreated }: { onCreated: (id: string) => void }) {
    const [parseError, setParseError] = useState<string | null>(null);
    const [dragOver, setDragOver] = useState(false);

    const createSession = usePostImport<Error>({
        mutation: {
            onSuccess: (result) => {
                const overview = result.data as ImportSessionOverview;
                if (overview?.id) onCreated(overview.id);
            },
        },
    });

    const handleFile = useCallback(
        async (file: File) => {
            setParseError(null);
            try {
                const parsed = JSON.parse(await file.text());
                createSession.mutate({
                    data: parsed,
                    params: { fileName: file.name },
                });
            } catch {
                setParseError(
                    `${file.name} is not valid JSON — expected a Gemini AI Studio export.`,
                );
            }
        },
        [createSession],
    );

    const busy = createSession.isPending;
    return (
        <div className="app-surface flex h-full flex-col items-center justify-center gap-3 p-6 text-[11px]">
            <label
                className="xp-glass-card flex w-full max-w-[420px] cursor-pointer flex-col items-center gap-3 p-6 text-center"
                style={{
                    border: dragOver
                        ? '2px dashed #316ac5'
                        : '2px dashed rgba(49,106,197,0.35)',
                }}
                onDragOver={(e) => {
                    e.preventDefault();
                    setDragOver(true);
                }}
                onDragLeave={() => setDragOver(false)}
                onDrop={(e) => {
                    e.preventDefault();
                    setDragOver(false);
                    const file = e.dataTransfer.files?.[0];
                    if (file) handleFile(file);
                }}
            >
                <span style={{ fontSize: '28px' }}>🗂️</span>
                <p className="font-semibold">Import Workshop</p>
                <p style={{ color: '#444' }}>
                    Drop a Gemini AI Studio export (.json) here, or pick a file.
                    Parsing is deterministic — no LLM runs until you run a
                    scene.
                </p>
                <span className="xp-glass-chip">
                    {busy ? 'Uploading…' : 'Choose file…'}
                </span>
                <input
                    type="file"
                    accept="application/json,.json"
                    className="hidden"
                    disabled={busy}
                    onChange={(e) => {
                        const file = e.target.files?.[0];
                        if (file) handleFile(file);
                        e.target.value = '';
                    }}
                />
                {busy ? <progress /> : null}
                {parseError ? (
                    <p style={{ color: '#c00' }}>{parseError}</p>
                ) : null}
                {createSession.isError ? (
                    <p style={{ color: '#c00' }}>
                        Upload failed: {createSession.error.message}
                    </p>
                ) : null}
            </label>
        </div>
    );
}
