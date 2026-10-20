/** Raw realtime envelopes relevant to World Apps (world.* + new chat messages). */
export interface WorldEnvelope {
    type: string;
    data: unknown;
}

type Listener = (envelope: WorldEnvelope) => void;

const listeners = new Set<Listener>();

export const worldBus = {
    emit(envelope: WorldEnvelope) {
        for (const listener of listeners) listener(envelope);
    },
    subscribe(listener: Listener) {
        listeners.add(listener);
        return () => {
            listeners.delete(listener);
        };
    },
};

/** Payload of a world.* envelope: `{ partyId, kind, payload }`. */
export interface WorldEventData<T = unknown> {
    partyId: string;
    kind: string;
    payload: T;
}
