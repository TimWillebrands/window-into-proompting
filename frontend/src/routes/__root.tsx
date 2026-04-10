/// <reference types="vite/client" />
import {
    createRootRoute,
    HeadContent,
    Outlet,
    Scripts,
} from '@tanstack/react-router';
import { TanStackRouterDevtools } from '@tanstack/react-router-devtools';
import type { ReactNode } from 'react';
import { Suspense } from 'react';
import CSSIsolation from '../components/CssIsolator';
import styleCss from '../styles.css?url';

export const Route = createRootRoute({
    head: () => ({
        links: [
            { rel: 'stylesheet', href: styleCss },
            { rel: 'stylesheet', href: '/XP.css' },
        ],
    }),
    component: RootComponent,
});

function RootComponent() {
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
        <html lang="en">
            <head>
                <HeadContent />
            </head>
            <body className="min-h-screen bg-slate-300">
                {children}
                <Scripts />
            </body>
        </html>
    );
}
