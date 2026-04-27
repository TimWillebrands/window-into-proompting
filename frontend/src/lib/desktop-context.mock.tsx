import type { ReactNode } from 'react';
import { fn } from 'storybook/test';
import type { DesktopLayoutState } from '../types/desktop';

const emptyLayout: DesktopLayoutState = {
    windows: {},
    order: [],
    focusedId: null,
    zCounter: 0,
    startMenuOpen: false,
};

export function DesktopProvider({ children }: { children: ReactNode }) {
    return <>{children}</>;
}

export const useDesktopContext = fn(() => ({
    desktopState: emptyLayout,
    openWindow: fn(),
    focusWindow: fn(),
    closeWindow: fn(),
    minimizeWindow: fn(),
    restoreWindow: fn(),
    toggleMaximize: fn(),
    updateSize: fn(),
    updatePosition: fn(),
    toggleStartMenu: fn(),
    closeStartMenu: fn(),
}));

export const useDesktopStore = Object.assign(
    fn((selector?: (state: unknown) => unknown) =>
        selector
            ? selector({ desktopState: emptyLayout, navigate: null })
            : { desktopState: emptyLayout, navigate: null },
    ),
    {
        getState: () => ({
            desktopState: emptyLayout,
            navigate: null,
            setNavigate: () => {},
            openWindow: () => {},
            focusWindow: () => {},
            closeWindow: () => {},
            minimizeWindow: () => {},
            restoreWindow: () => {},
            toggleMaximize: () => {},
            updateSize: () => {},
            updatePosition: () => {},
            toggleStartMenu: () => {},
            closeStartMenu: () => {},
        }),
        setState: fn(),
        subscribe: fn(() => () => {}),
    },
);
