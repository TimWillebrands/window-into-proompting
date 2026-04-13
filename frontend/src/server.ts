import handler, { createServerEntry } from '@tanstack/react-start/server-entry';
import { FastResponse } from 'srvx';
globalThis.Response = FastResponse;

const serverEntry = createServerEntry({
    async fetch(request) {
        return await handler.fetch(request);
    },
});

export default {
    ...serverEntry,
};
