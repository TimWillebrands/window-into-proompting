import { defineConfig } from 'orval';

export default defineConfig({
    partyzone: {
        input: {
            target: '../backend/openapi.json',
        },
        output: {
            target: './src/api/party-zone.ts',
            schemas: './src/api/model',
            client: 'react-query',
            baseUrl: '/api',
            biome: true,
            override: {
                query: {
                    useSuspenseQuery: true,
                },
                mutator: {
                    path: './src/api/custom-fetch.ts',
                    name: 'customFetch',
                },
            },
        },
    },
});
