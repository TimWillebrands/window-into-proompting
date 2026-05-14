/**
 * Direct fetch wrappers for /api/Import REST endpoints.
 *
 * Not generated via Orval because the WS endpoint isn't representable in OpenAPI
 * and keeping the two REST endpoints colocated with their hand-written types
 * avoids splitting the import surface across two import paths.
 */

export type ExtractedPersona = {
    name: string;
    archetype: string | null;
    system_prompt: string;
    bio: string | null;
};

export type ExtractSampleChunk = {
    role: 'user' | 'model';
    text: string;
};

export async function extractPersonas(
    systemInstruction: string,
    sampleChunks: ExtractSampleChunk[],
    signal?: AbortSignal,
): Promise<ExtractedPersona[]> {
    const res = await fetch('/api/Import/extract-personas', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ systemInstruction, sampleChunks }),
        signal,
    });
    if (!res.ok) {
        throw new Error(
            `extract-personas failed: ${res.status} ${await res.text()}`,
        );
    }
    const json = (await res.json()) as { personas: ExtractedPersona[] };
    return json.personas;
}

export async function mergePersonas(
    personas: ExtractedPersona[],
    signal?: AbortSignal,
): Promise<ExtractedPersona> {
    const res = await fetch('/api/Import/merge-personas', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ personas }),
        signal,
    });
    if (!res.ok) {
        throw new Error(
            `merge-personas failed: ${res.status} ${await res.text()}`,
        );
    }
    const json = (await res.json()) as { persona: ExtractedPersona };
    return json.persona;
}

export type CommitPersonaStub = {
    placeholderId: string;
    name: string;
    systemPrompt: string;
    bio: string | null;
    isNew: boolean;
    preExistingId: string | null;
};

export type CommitMessage = {
    senderPlaceholderId: string;
    senderType: 'user' | 'assistant';
    content: string;
    sendAt: number;
    kind: string | null;
};

export type CommitImportRequest = {
    partyId: string;
    chatGroupName: string;
    scenario: string | null;
    personas: CommitPersonaStub[];
    messages: CommitMessage[];
    userParticipant: { id: string; name: string } | null;
};

export type CommitImportResponse = {
    chatGroupId: string;
    createdPersonaIds: string[];
    messagesWritten: number;
};

export async function commitImport(
    body: CommitImportRequest,
    signal?: AbortSignal,
): Promise<CommitImportResponse> {
    const res = await fetch('/api/Import/commit', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
        signal,
    });
    if (!res.ok) {
        throw new Error(
            `commit failed: ${res.status} ${await res.text()}`,
        );
    }
    return (await res.json()) as CommitImportResponse;
}
