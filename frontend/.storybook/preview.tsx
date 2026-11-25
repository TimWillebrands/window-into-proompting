import { ProgressBar } from '@partytown/ui';
import type { Decorator, Preview } from '@storybook/react-vite';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Suspense } from 'react';
import '@partytown/ui/styles.css';
import '../src/styles.css';
import { useThemeStore } from '../src/lib/theme-store';

const queryClient = new QueryClient({
    defaultOptions: {
        queries: { retry: false, staleTime: Infinity, gcTime: Infinity },
        mutations: { retry: false },
    },
});

const withScheme: Decorator = (Story, context) => {
    useThemeStore
        .getState()
        .set(context.globals.scheme === 'noir' ? 'dark' : 'light');
    return <Story />;
};

const preview: Preview = {
    parameters: {
        layout: 'centered',
        controls: {
            matchers: { color: /(background|color)$/i, date: /Date$/i },
        },
        backgrounds: {
            options: {
                'xp-desktop': {
                    name: 'XP desktop',
                    value: 'url(/img/blissful_penger.webp) center / cover no-repeat fixed',
                },
                'window-body': { name: 'XP face', value: 'var(--ui-face)' },
                content: { name: 'Content', value: 'var(--ui-content)' },
            },
        },
    },
    globalTypes: {
        scheme: {
            description: 'Colour scheme',
            toolbar: {
                title: 'Scheme',
                icon: 'paintbrush',
                items: [
                    { value: 'luna-blue', title: 'Luna Blue' },
                    { value: 'noir', title: 'Noir' },
                ],
                dynamicTitle: true,
            },
        },
    },
    initialGlobals: {
        scheme: 'luna-blue',
        backgrounds: { value: 'xp-desktop' },
    },
    decorators: [
        withScheme,
        (Story) => (
            <QueryClientProvider client={queryClient}>
                <Suspense fallback={<ProgressBar marquee label="Loading" />}>
                    <Story />
                </Suspense>
            </QueryClientProvider>
        ),
    ],
};

export default preview;
