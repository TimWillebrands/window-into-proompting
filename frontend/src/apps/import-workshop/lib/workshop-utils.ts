/// Shared helpers for the import workshop: numeric coercion for the generated
/// client's `number | string` fields, category/disposition palettes, invalidation.
import type { QueryClient } from '@tanstack/react-query';
import {
    getGetImportIdDraftQueryKey,
    getGetImportIdLedgerQueryKey,
    getGetImportIdQueryKey,
    getGetImportIdRegistryQueryKey,
} from '#api/party-zone';

export function num(
    value: number | string | null | undefined,
    fallback = 0,
): number {
    if (value == null) return fallback;
    const n = typeof value === 'number' ? value : Number(value);
    return Number.isFinite(n) ? n : fallback;
}

export const CATEGORY_COLORS: Record<string, string> = {
    message: '#4a7ebb',
    recap: '#8e5bb5',
    thought: '#9aa0a6',
    media: '#c9962e',
    empty: '#d0d0d0',
};

export const DISPOSITION_COLORS: Record<string, string> = {
    'event-routed': '#2e8b57',
    folded: '#2aa198',
    'history-only': '#708090',
    discarded: '#c0392b',
    unprocessed: '#b8b8b8',
};

export function categoryColor(category: string | undefined): string {
    return CATEGORY_COLORS[category ?? ''] ?? '#d0d0d0';
}

export function dispositionColor(disposition: string | undefined): string {
    return DISPOSITION_COLORS[disposition ?? ''] ?? '#b8b8b8';
}

/// Everything a scene run, edit or commit can move: overview, draft, ledger, registry.
export function invalidateImportSession(
    queryClient: QueryClient,
    sessionId: string,
): void {
    queryClient.invalidateQueries({
        queryKey: getGetImportIdQueryKey(sessionId),
    });
    queryClient.invalidateQueries({
        queryKey: getGetImportIdDraftQueryKey(sessionId),
    });
    queryClient.invalidateQueries({
        queryKey: getGetImportIdLedgerQueryKey(sessionId),
    });
    queryClient.invalidateQueries({
        queryKey: getGetImportIdRegistryQueryKey(sessionId),
    });
}

const SESSION_KEY = 'import-workshop.sessionId';

export function loadStoredSessionId(): string | null {
    if (typeof window === 'undefined') return null;
    return window.localStorage.getItem(SESSION_KEY);
}

export function storeSessionId(sessionId: string | null): void {
    if (typeof window === 'undefined') return;
    if (sessionId) window.localStorage.setItem(SESSION_KEY, sessionId);
    else window.localStorage.removeItem(SESSION_KEY);
}
