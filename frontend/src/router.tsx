import { MutationCache, QueryClient } from '@tanstack/react-query';
import { createRouter, ErrorComponent } from '@tanstack/react-router';
import { setupRouterSsrQueryIntegration } from '@tanstack/react-router-ssr-query';
import { ProblemDetailsError } from './api/custom-fetch';
import { parseSearch, stringifySearch } from './lib/search-codec';
import { notifyOnRefusal } from './lib/world';
import { routeTree } from './routeTree.gen';

const refused = (error: unknown) => {
    if (!(error instanceof ProblemDetailsError)) return false;
    const status = Number(error.status);
    return status >= 400 && status < 500 && status !== 408 && status !== 429;
};

const retry = (failures: number, error: unknown) =>
    failures < 3 && !refused(error);

export function getRouter() {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry } },
        mutationCache: new MutationCache({ onError: notifyOnRefusal }),
    });

    const router = createRouter({
        routeTree,
        scrollRestoration: true,
        defaultPreload: 'intent',
        defaultErrorComponent: ({ error }) => {
            console.error(error);
            return <ErrorComponent error={error} />;
        },
        context: { queryClient },
        parseSearch,
        stringifySearch,
    });

    setupRouterSsrQueryIntegration({
        router,
        queryClient,
    });

    return router;
}
