import handler, { createServerEntry } from '@tanstack/react-start/server-entry';

const serverEntry = createServerEntry({
    async fetch(request) {
        return await handler.fetch(request);
    },
});

export default {
    ...serverEntry,
};
