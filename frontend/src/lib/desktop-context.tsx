import {
    emptyDesktop,
    type LaunchOptions,
    launchProgram,
    type WindowAction,
    windowActions,
    windowReducer,
} from '@partytown/ui';
import { useNavigate } from '@tanstack/react-router';
import { type ReactNode, useEffect } from 'react';
import { create } from 'zustand';
import type { DesktopLayoutState } from '../types/desktop';
import { track } from './analytics';
import { useNavHistoryStore } from './nav-history';
import { desktopPrograms } from './window-presets';

type Navigate = (args: { search: unknown; replace?: boolean }) => void;

type DispatchOptions = { replace?: boolean };

interface OpenOptions {
    instanceId?: string;
    title?: string;
    icon?: string;
    props?: Record<string, unknown>;
}

interface DesktopStoreState {
    desktopState: DesktopLayoutState;
    navigate: Navigate | null;
    setNavigate: (navigate: Navigate) => void;
    dispatch: (action: WindowAction, opts?: DispatchOptions) => void;
    openWindow: (appId: string, options?: OpenOptions) => void;
    focusWindow: (id: string) => void;
    closeWindow: (id: string) => void;
    setWindowProps: (
        id: string,
        update: (prev: Record<string, unknown>) => Record<string, unknown>,
        opts?: DispatchOptions,
    ) => void;
    setWindowTitle: (id: string, title: string, icon?: string) => void;
    toggleStartMenu: () => void;
    closeStartMenu: () => void;
}

function remember(
    prev: DesktopLayoutState,
    next: DesktopLayoutState,
    action: WindowAction,
) {
    if ((action.type === 'open' || action.type === 'focus') && next.focusedId)
        useNavHistoryStore
            .getState()
            .push({ kind: 'window', windowId: next.focusedId });
    for (const id of Object.keys(next.windows))
        if (!prev.windows[id])
            track('window_opened', { app_id: next.windows[id]?.appId });
    for (const id of Object.keys(prev.windows))
        if (!next.windows[id])
            track('window_closed', { app_id: prev.windows[id]?.appId });
}

export const useDesktopStore = create<DesktopStoreState>((set, get) => {
    const dispatch = (action: WindowAction, opts?: DispatchOptions) => {
        const prev = get().desktopState;
        const next = windowReducer(prev, action);
        if (next === prev) return;
        remember(prev, next, action);
        get().navigate?.({ search: () => next, replace: opts?.replace });
        set({ desktopState: next });
    };

    return {
        desktopState: emptyDesktop,
        navigate: null,
        setNavigate: (navigate) => set({ navigate }),
        dispatch,
        openWindow: (appId, options = {}) => {
            const program = desktopPrograms.find((entry) => entry.id === appId);
            if (!program) return;
            const launch: LaunchOptions = {
                id: options.instanceId,
                title: options.title,
                icon: options.icon,
                props: options.props,
            };
            dispatch(launchProgram(program, launch));
        },
        focusWindow: (id) => dispatch(windowActions.focus(id)),
        closeWindow: (id) => dispatch(windowActions.close(id)),
        setWindowProps: (id, update, opts) => {
            const window = get().desktopState.windows[id];
            if (!window) return;
            dispatch(windowActions.setProps(id, update(window.props)), opts);
        },
        setWindowTitle: (id, title, icon) =>
            dispatch(windowActions.setTitle(id, title, icon), {
                replace: true,
            }),
        toggleStartMenu: () => dispatch(windowActions.startMenu()),
        closeStartMenu: () => dispatch(windowActions.startMenu(false)),
    };
});

export function DesktopProvider({ children }: { children: ReactNode }) {
    const navigate = useNavigate();

    useEffect(() => {
        useDesktopStore.getState().setNavigate(navigate as Navigate);
    }, [navigate]);

    return <>{children}</>;
}

export function useDesktopContext() {
    const desktopState = useDesktopStore((state) => state.desktopState);
    const dispatch = useDesktopStore((state) => state.dispatch);
    const openWindow = useDesktopStore((state) => state.openWindow);
    const focusWindow = useDesktopStore((state) => state.focusWindow);
    const closeWindow = useDesktopStore((state) => state.closeWindow);
    const setWindowProps = useDesktopStore((state) => state.setWindowProps);
    const setWindowTitle = useDesktopStore((state) => state.setWindowTitle);
    const toggleStartMenu = useDesktopStore((state) => state.toggleStartMenu);
    const closeStartMenu = useDesktopStore((state) => state.closeStartMenu);

    return {
        desktopState,
        dispatch,
        openWindow,
        focusWindow,
        closeWindow,
        setWindowProps,
        setWindowTitle,
        toggleStartMenu,
        closeStartMenu,
    };
}
