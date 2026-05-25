import { useCallback, useRef } from 'react';
import {
    type ImportIdentifyMsg,
    importIdentifyWsStream,
} from '../../../lib/import-ws-stream';
import { type RawMention, useImportStore } from '../state/import-store';

type IdentifyMapPayload = {
    msgId: string;
    completed: number;
    total: number;
    mentions: RawMention[];
};

type IdentifyRosterPayload = {
    characters: {
        id: string;
        primary_name: string;
        names: string[];
        archetype: string | null;
    }[];
    links: { msgId: string; mentionId: string; charId: string }[];
};

type IdentifyReducePayload = {
    characterId: string;
    prompt: string;
    bio: string | null;
};

type IdentifyErrorPayload = { error?: string };

/**
 * Drives the /api/Import/identify-ws stream. Caller invokes `runIdentify(msgs, sysInstruction)`
 * after toggling phase status to running; this hook dispatches actions on each
 * envelope and clears the run state on completion or error.
 */
export function useIdentifyWs() {
    const abortRef = useRef<AbortController | null>(null);
    const beginIdentify = useImportStore((s) => s.beginIdentify);
    const onMap = useImportStore((s) => s.onIdentifyMapProgress);
    const onRoster = useImportStore((s) => s.onIdentifyRosterSync);
    const onReduce = useImportStore((s) => s.onIdentifyReduceProgress);
    const onCompleted = useImportStore((s) => s.onIdentifyCompleted);
    const onError = useImportStore((s) => s.onIdentifyError);

    const cancel = useCallback(() => {
        abortRef.current?.abort();
        abortRef.current = null;
    }, []);

    const runIdentify = useCallback(
        async (systemInstructionText: string, msgs: ImportIdentifyMsg[]) => {
            cancel();
            const ac = new AbortController();
            abortRef.current = ac;
            beginIdentify(msgs.map((m) => m.id));
            try {
                for await (const envelope of importIdentifyWsStream(
                    { systemInstructionText, msgs },
                    ac.signal,
                )) {
                    if (envelope.type === 'import.identify.map_progress') {
                        const d = envelope.data as IdentifyMapPayload;
                        onMap(d.msgId, d.mentions);
                    } else if (
                        envelope.type === 'import.identify.roster_sync'
                    ) {
                        const d = envelope.data as IdentifyRosterPayload;
                        onRoster(d.characters, d.links);
                    } else if (
                        envelope.type === 'import.identify.reduce_progress'
                    ) {
                        const d = envelope.data as IdentifyReducePayload;
                        onReduce(d.characterId, d.prompt, d.bio);
                    } else if (envelope.type === 'import.identify.completed') {
                        onCompleted();
                        break;
                    } else if (envelope.type === 'import.identify.error') {
                        const d = envelope.data as IdentifyErrorPayload;
                        onError(d.error ?? 'Identify failed.');
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
        [
            beginIdentify,
            cancel,
            onCompleted,
            onError,
            onMap,
            onReduce,
            onRoster,
        ],
    );

    return { runIdentify, cancel };
}
