import { useCallback, useRef } from 'react';
import {
    type ImportChunkInput,
    type ImportRosterEntry,
    importClassifyWsStream,
} from '../../../lib/import-ws-stream';
import {
    type ClassifierSegmentInput,
    useImportStore,
} from '../state/import-store';

type ClassifyProgressPayload = {
    chunkId: string;
    completed: number;
    total: number;
    segments: ClassifierSegmentInput[];
};
type ClassifyErrorPayload = { error?: string };

/**
 * Drives /api/Import/classify-ws. Caller passes msgIds + builds the chunk
 * payloads upstream (so we don't reach back into the parsed file from a hook).
 */
export function useClassifyWs() {
    const abortRef = useRef<AbortController | null>(null);
    const beginClassify = useImportStore((s) => s.beginClassify);
    const onProgress = useImportStore((s) => s.onClassifyProgress);
    const onCompleted = useImportStore((s) => s.onClassifyCompleted);
    const onError = useImportStore((s) => s.onClassifyError);

    const cancel = useCallback(() => {
        abortRef.current?.abort();
        abortRef.current = null;
    }, []);

    const runClassify = useCallback(
        async (chunks: ImportChunkInput[], roster: ImportRosterEntry[]) => {
            cancel();
            const ac = new AbortController();
            abortRef.current = ac;
            beginClassify(chunks.map((c) => c.id));
            try {
                for await (const envelope of importClassifyWsStream(
                    { chunks, roster },
                    ac.signal,
                )) {
                    if (envelope.type === 'import.classify.progress') {
                        const d = envelope.data as ClassifyProgressPayload;
                        onProgress(d.chunkId, d.segments);
                    } else if (envelope.type === 'import.classify.completed') {
                        onCompleted();
                        break;
                    } else if (envelope.type === 'import.classify.error') {
                        const d = envelope.data as ClassifyErrorPayload;
                        onError(d.error ?? 'Classify failed.');
                        return;
                    }
                }
            } catch (err) {
                if (err instanceof DOMException && err.name === 'AbortError')
                    return;
                onError(err instanceof Error ? err.message : String(err));
            } finally {
                abortRef.current = null;
            }
        },
        [beginClassify, cancel, onCompleted, onError, onProgress],
    );

    return { runClassify, cancel };
}
