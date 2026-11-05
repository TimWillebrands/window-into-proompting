import handler, { createServerEntry } from '@tanstack/react-start/server-entry';
import { FastResponse } from 'srvx';
import { proxyPostHog } from './lib/posthog-proxy';

globalThis.Response = FastResponse;

const serverEntry = createServerEntry({
    async fetch(request) {
        return (
            (await proxyPostHog(request, process.env.POSTHOG_HOST)) ??
            (await handler.fetch(request))
        );
    },
});

export default {
    ...serverEntry,
};
