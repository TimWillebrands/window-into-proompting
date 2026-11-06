import { MutationCache, QueryClient } from '@tanstack/react-query';
import { createRouter, ErrorComponent } from '@tanstack/react-router';
import { setupRouterSsrQueryIntegration } from '@tanstack/react-router-ssr-query';
import { toastOnAuto } from './lib/world';
import { routeTree } from './routeTree.gen';

export function getRouter() {
    const queryClient = new QueryClient({
        mutationCache: new MutationCache({ onError: toastOnAuto }),
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
    });

    setupRouterSsrQueryIntegration({
        router,
        queryClient, // This type error is harmless (shrug)
    });

    return router;
}
