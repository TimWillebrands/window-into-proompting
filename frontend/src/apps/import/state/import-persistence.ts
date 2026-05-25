import {
    type PersistedSnapshot,
    type StoreState,
    useImportStore,
} from './import-store';

const KEY_PREFIX = 'import:';
const MAX_BYTES = 4 * 1024 * 1024;
const DEBOUNCE_MS = 500;

export function snapshotKey(hash: string): string {
    return `${KEY_PREFIX}${hash}`;
}

export function loadSnapshot(hash: string): PersistedSnapshot | null {
    try {
        const raw = localStorage.getItem(snapshotKey(hash));
        if (!raw) return null;
        return JSON.parse(raw) as PersistedSnapshot;
    } catch {
        return null;
    }
}

export function clearSnapshot(hash: string): void {
    try {
        localStorage.removeItem(snapshotKey(hash));
    } catch {
        /* quota or disabled storage; nothing to do */
    }
}

function buildSnapshot(s: StoreState): PersistedSnapshot {
    const msgPatches: PersistedSnapshot['msgPatches'] = {};
    for (const id of s.msgOrder) {
        const m = s.msgs[id];
        if (!m) continue;
        // Only persist msgs that have non-default state — default-included and
        // pending status with no participants/segments stays implicit.
        const hasState =
            m.participants.length > 0 ||
            m.segments.length > 0 ||
            m.droppedSegmentIds.length > 0 ||
            m.identifyStatus !== 'pending' ||
            m.classifyStatus !== 'pending' ||
            !m.included;
        if (!hasState) continue;
        msgPatches[id] = {
            included: m.included,
            participants: m.participants,
            segments: m.segments,
            droppedSegmentIds: m.droppedSegmentIds,
            identifyStatus: m.identifyStatus,
            classifyStatus: m.classifyStatus,
        };
    }
    return {
        msgPatches,
        roster: s.roster,
        metadata: s.metadata,
        phase: s.phase,
    };
}

let timer: ReturnType<typeof setTimeout> | null = null;
let lastNotifiedTooBig = false;

/**
 * Wire the Zustand store to localStorage. Returns an unsubscribe function.
 * Writes are debounced; oversized snapshots (> ~4 MB) are skipped with a
 * one-shot console warning.
 */
export function startPersistence(): () => void {
    const unsubscribe = useImportStore.subscribe((state, prev) => {
        if (!state.fileHash) return;
        // Skip pure-UI changes — comparing the relevant slices is cheaper than
        // serialising on every mouse move.
        if (
            state.msgs === prev.msgs &&
            state.roster === prev.roster &&
            state.metadata === prev.metadata &&
            state.phase === prev.phase
        ) {
            return;
        }
        if (timer !== null) clearTimeout(timer);
        timer = setTimeout(() => {
            timer = null;
            const hash = useImportStore.getState().fileHash;
            if (!hash) return;
            try {
                const json = JSON.stringify(
                    buildSnapshot(useImportStore.getState()),
                );
                if (json.length > MAX_BYTES) {
                    if (!lastNotifiedTooBig) {
                        lastNotifiedTooBig = true;
                        console.warn(
                            `[import] draft too large to autosave (${(json.length / 1024 / 1024).toFixed(2)} MB > ${MAX_BYTES / 1024 / 1024} MB cap)`,
                        );
                    }
                    return;
                }
                lastNotifiedTooBig = false;
                localStorage.setItem(snapshotKey(hash), json);
            } catch (err) {
                console.warn('[import] persistence write failed', err);
            }
        }, DEBOUNCE_MS);
    });
    return () => {
        if (timer !== null) clearTimeout(timer);
        unsubscribe();
    };
}
