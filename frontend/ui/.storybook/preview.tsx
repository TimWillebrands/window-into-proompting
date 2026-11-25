import type { Decorator, Preview } from '@storybook/react-vite';
import '../src/styles.css';
import { type Scheme, setScheme } from '../src/scheme';

const flag = (name: string, on: boolean) => {
    if (on) document.documentElement.setAttribute(name, '');
    else document.documentElement.removeAttribute(name);
};

const withUiRoot: Decorator = (Story, context) => {
    const { scheme, reduceMotion, reduceTransparency, contrast } =
        context.globals;
    setScheme((scheme as Scheme) ?? 'luna-blue');
    flag('data-ui-reduce-motion', reduceMotion === 'on');
    flag('data-ui-reduce-transparency', reduceTransparency === 'on');
    if (contrast === 'more')
        document.documentElement.setAttribute('data-ui-contrast', 'more');
    else document.documentElement.removeAttribute('data-ui-contrast');
    return <Story />;
};

const toggle = (title: string, icon: string) => ({
    description: title,
    toolbar: {
        title,
        icon,
        items: [
            { value: 'off', title: `${title}: off` },
            { value: 'on', title: `${title}: on` },
        ],
        dynamicTitle: true,
    },
});

const preview: Preview = {
    parameters: {
        layout: 'centered',
        a11y: {
            test: 'error',
            options: {
                runOnly: [
                    'wcag2a',
                    'wcag2aa',
                    'wcag21a',
                    'wcag21aa',
                    'wcag22aa',
                ],
            },
            config: {
                rules: [
                    {
                        id: 'color-contrast',
                        selector:
                            '*:not([data-disabled]):not([aria-disabled="true"]):not(:disabled)',
                    },
                ],
            },
        },
        backgrounds: {
            options: {
                desktop: {
                    name: 'XP desktop',
                    value: 'url(/backdrops/bliss.svg) center / cover no-repeat fixed',
                },
                face: { name: 'XP face', value: 'var(--ui-face)' },
                busy: {
                    name: 'Busy (for glass)',
                    value: 'url(/backdrops/busy.svg) center / cover no-repeat fixed',
                },
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
                    { value: 'olive-green', title: 'Olive Green' },
                    { value: 'silver', title: 'Silver' },
                    { value: 'noir', title: 'Noir' },
                ],
                dynamicTitle: true,
            },
        },
        reduceMotion: toggle('Reduce motion', 'lightning'),
        reduceTransparency: toggle('Reduce transparency', 'eye'),
        contrast: {
            description: 'Increase contrast',
            toolbar: {
                title: 'Contrast',
                icon: 'contrast',
                items: [
                    { value: 'normal', title: 'Contrast: normal' },
                    { value: 'more', title: 'Contrast: more' },
                ],
                dynamicTitle: true,
            },
        },
    },
    initialGlobals: {
        scheme: 'luna-blue',
        reduceMotion: 'off',
        reduceTransparency: 'off',
        contrast: 'normal',
        backgrounds: { value: 'desktop' },
    },
    decorators: [withUiRoot],
};

export default preview;
