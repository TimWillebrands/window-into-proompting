import { createRouter, ErrorComponent } from '@tanstack/react-router';
import { setupRouterSsrQueryIntegration } from '@tanstack/react-router-ssr-query';
import { routeTree } from './routeTree.gen';
import { QueryClient } from '@tanstack/react-query';

export function getRouter() {
    const queryClient = new QueryClient();

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
