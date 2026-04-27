import { fn } from 'storybook/test';

type WsEnvelope = {
    type: string;
    sequence: number;
    timestamp: number;
    data: unknown;
};

const stockBio = `Indie hacker. Ships fast. Trusts code, distrusts pomp.`;

async function* defaultStream(): AsyncGenerator<WsEnvelope, void, unknown> {
    let seq = 0;
    const chunks = stockBio.split(/(\s+)/);
    for (const chunk of chunks) {
        seq += 1;
        await new Promise((r) => setTimeout(r, 50));
        yield {
            type: 'persona.generation.delta',
            sequence: seq,
            timestamp: Date.now(),
            data: { data: chunk },
        };
    }
    yield {
        type: 'persona.generation.completed',
        sequence: seq + 1,
        timestamp: Date.now(),
        data: {
            name: 'Generated Persona',
            systemPrompt: 'You are a generated persona.',
            bio: stockBio,
        },
    };
}

export const personaWsStream = fn(async function* (
    _request: { type: string; [key: string]: unknown },
    _signal?: AbortSignal,
): AsyncGenerator<WsEnvelope, void, unknown> {
    yield* defaultStream();
});
