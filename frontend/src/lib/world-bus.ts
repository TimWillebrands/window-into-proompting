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

export interface WorldEventData<T = unknown> {
    partyId: string;
    kind: string;
    payload: T;
}
