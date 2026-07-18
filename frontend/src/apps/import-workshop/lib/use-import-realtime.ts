/// Realtime channel for one import session (`/api/import/{id}/ws`): scene-run
/// lifecycle + pre-fold item previews. Reconnects with backoff; the on-connect
/// snapshot lets a page refreshed mid-run reattach to live progress.
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { invalidateImportSession } from './workshop-utils';

export interface RunItemPreview {
    type?: string | null;
    persona?: string | null;
    summary?: string;
    weight?: number | null;
}

export interface SceneRunProgress {
    running: boolean;
    callsDone: number;
    totalCalls: number;
    stage: string | null;
    error: string | null;
    previews: RunItemPreview[];
}

export type ImportRealtimeStatus =
    | 'connecting'
    | 'connected'
    | 'reconnecting'
    | 'disconnected';

interface Envelope {
    type: string;
    data: Record<string, unknown>;
}

const MAX_PREVIEWS = 60;

function buildWsUrl(sessionId: string): string {
    const base = new URL(`/api/import/${sessionId}/ws`, window.location.origin);
    base.protocol = base.protocol === 'https:' ? 'wss:' : 'ws:';
    return base.toString();
}

export function useImportRealtime(sessionId: string): {
    status: ImportRealtimeStatus;
    runs: Record<string, SceneRunProgress>;
} {
    const queryClient = useQueryClient();
    const [status, setStatus] = useState<ImportRealtimeStatus>('connecting');
    const [runs, setRuns] = useState<Record<string, SceneRunProgress>>({});
    const queryClientRef = useRef(queryClient);
    queryClientRef.current = queryClient;

    useEffect(() => {
        let socket: WebSocket | null = null;
        let closed = false;
        let attempt = 0;
        let retryTimer: ReturnType<typeof setTimeout> | null = null;

        const patchRun = (
            sceneId: string,
            patch: Partial<SceneRunProgress>,
            appendPreviews?: RunItemPreview[],
        ) => {
            setRuns((prev) => {
                const existing: SceneRunProgress = prev[sceneId] ?? {
                    running: false,
                    callsDone: 0,
                    totalCalls: 0,
                    stage: null,
                    error: null,
                    previews: [],
                };
                const previews = appendPreviews?.length
                    ? [...existing.previews, ...appendPreviews].slice(
                          -MAX_PREVIEWS,
                      )
                    : existing.previews;
                return {
                    ...prev,
                    [sceneId]: { ...existing, ...patch, previews },
                };
            });
        };

        const handleEnvelope = (envelope: Envelope) => {
            const data = envelope.data ?? {};
            const sceneId =
                typeof data.sceneId === 'string' ? data.sceneId : null;
            switch (envelope.type) {
                case 'import.run.snapshot': {
                    const active = Array.isArray(data.runs) ? data.runs : [];
                    for (const run of active as Array<
                        Record<string, unknown>
                    >) {
                        if (typeof run.sceneId !== 'string') continue;
                        patchRun(run.sceneId, {
                            running: true,
                            callsDone: Number(run.callsDone ?? 0),
                            totalCalls: Number(run.totalCalls ?? 0),
                            error: null,
                        });
                    }
                    break;
                }
                case 'import.run.started':
                    if (sceneId)
                        patchRun(sceneId, {
                            running: true,
                            callsDone: 0,
                            totalCalls: 0,
                            stage: null,
                            error: null,
                        });
                    break;
                case 'import.run.progress':
                    if (sceneId)
                        patchRun(
                            sceneId,
                            {
                                running: true,
                                callsDone: Number(data.callsDone ?? 0),
                                totalCalls: Number(data.totalCalls ?? 0),
                                stage:
                                    typeof data.stage === 'string'
                                        ? data.stage
                                        : null,
                                error: null,
                            },
                            Array.isArray(data.items)
                                ? (data.items as RunItemPreview[])
                                : undefined,
                        );
                    break;
                case 'import.run.completed':
                    if (sceneId)
                        patchRun(sceneId, { running: false, error: null });
                    invalidateImportSession(queryClientRef.current, sessionId);
                    break;
                case 'import.run.failed':
                    if (sceneId)
                        patchRun(sceneId, {
                            running: false,
                            error:
                                typeof data.error === 'string'
                                    ? data.error
                                    : 'run failed',
                        });
                    invalidateImportSession(queryClientRef.current, sessionId);
                    break;
                default:
                    break;
            }
        };

        const connect = () => {
            if (closed) return;
            setStatus(attempt === 0 ? 'connecting' : 'reconnecting');
            socket = new WebSocket(buildWsUrl(sessionId));
            socket.addEventListener('open', () => {
                attempt = 0;
                setStatus('connected');
            });
            socket.addEventListener('message', (event) => {
                try {
                    handleEnvelope(JSON.parse(String(event.data)) as Envelope);
                } catch {
                    // non-JSON frames are ignored
                }
            });
            socket.addEventListener('close', () => {
                if (closed) return;
                attempt += 1;
                setStatus('reconnecting');
                retryTimer = setTimeout(
                    connect,
                    Math.min(1000 * 2 ** attempt, 10_000),
                );
            });
            socket.addEventListener('error', () => {
                socket?.close();
            });
        };

        connect();

        return () => {
            closed = true;
            if (retryTimer) clearTimeout(retryTimer);
            setStatus('disconnected');
            socket?.close();
        };
    }, [sessionId]);

    return { status, runs };
}
