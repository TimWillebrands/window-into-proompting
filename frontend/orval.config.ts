import { defineConfig } from 'orval';

export default defineConfig({
    partyzone: {
        input: {
            target: [
                'http://localhost:8080/api/openapi/v1.json',
                'http://localhost:5072/api/openapi/v1.json',
                'http://backend:5072/api/openapi/v1.json',
            ],
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
