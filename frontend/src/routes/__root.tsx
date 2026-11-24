/// <reference types="vite/client" />
import { ClerkProvider } from '@clerk/tanstack-react-start';
import uiCss from '@partytown/ui/styles.css?url';
import {
    createRootRoute,
    HeadContent,
    Outlet,
    Scripts,
} from '@tanstack/react-router';
import { TanStackRouterDevtools } from '@tanstack/react-router-devtools';
import type { ReactNode } from 'react';
import { Suspense, useEffect } from 'react';
import CSSIsolation from '../components/CssIsolator';
import CookieBalloon from '../components/desktop/CookieBalloon';
import { schemeScript, useThemeStore } from '../lib/theme-store';
import styleCss from '../styles.css?url';

export const Route = createRootRoute({
    head: () => ({
        links: [
            { rel: 'stylesheet', href: uiCss },
            { rel: 'stylesheet', href: styleCss },
        ],
    }),
    component: RootComponent,
});

function RootComponent() {
    useEffect(() => {
        void useThemeStore.persist.rehydrate();
    }, []);
    return (
        <RootDocument>
            <Suspense>
                <Outlet />
            </Suspense>
            <CSSIsolation>
                <TanStackRouterDevtools position="top-right" />
            </CSSIsolation>
        </RootDocument>
    );
}

function RootDocument({ children }: Readonly<{ children: ReactNode }>) {
    return (
        <html lang="en" suppressHydrationWarning>
            <head>
                <script
                    // biome-ignore lint/security/noDangerouslySetInnerHtml: the saved scheme has to reach <html> before the first paint
                    dangerouslySetInnerHTML={{ __html: schemeScript }}
                />
                <HeadContent />
            </head>
            <body className="min-h-screen">
                <ClerkProvider>{children}</ClerkProvider>
                <CookieBalloon />
                <Scripts />
            </body>
        </html>
    );
}
