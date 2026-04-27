import type { Preview } from '@storybook/react-vite';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Suspense } from 'react';
import '../src/styles.css';

const queryClient = new QueryClient({
    defaultOptions: {
        queries: { retry: false, staleTime: Infinity, gcTime: Infinity },
        mutations: { retry: false },
    },
});

const preview: Preview = {
    parameters: {
        layout: 'centered',
        controls: {
            matchers: { color: /(background|color)$/i, date: /Date$/i },
        },
        backgrounds: {
            default: 'xp-desktop',
            values: [
                { name: 'xp-desktop', value: '#5C87C2' },
                { name: 'window-body', value: '#ECE9D8' },
                { name: 'white', value: '#fff' },
            ],
        },
    },
    decorators: [
        (Story) => (
            <QueryClientProvider client={queryClient}>
                <Suspense fallback={<progress>Loading...</progress>}>
                    <Story />
                </Suspense>
            </QueryClientProvider>
        ),
    ],
};

export default preview;
