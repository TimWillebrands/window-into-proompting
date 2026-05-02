import { useNavigate } from '@tanstack/react-router';
import { type ReactNode, useEffect } from 'react';
import { create } from 'zustand';
import type { DesktopLayoutState } from '../types/desktop';
import { useNavHistoryStore } from './nav-history';
import type { DesktopWindowState, WindowDescriptor } from './types';
import { WINDOW_PRESETS } from './window-presets';

interface DesktopContextInternal {
    desktopState: DesktopLayoutState;
    openWindow: (
        appId: string,
        options?: {
            instanceId?: string;
            title?: string;
            icon?: string;
            props?: Record<string, unknown>;
        },
    ) => void;
    focusWindow: (id: string) => void;
    closeWindow: (id: string) => void;
    minimizeWindow: (id: string) => void;
    restoreWindow: (id: string) => void;
    toggleMaximize: (id: string) => void;
    updateSize: (id: string, size: { width: number; height: number }) => void;
    updatePosition: (id: string, position: { x: number; y: number }) => void;
    setWindowProps: (
        id: string,
        update: (prev: Record<string, unknown>) => Record<string, unknown>,
        opts?: { replace?: boolean },
    ) => void;
    toggleStartMenu: () => void;
    closeStartMenu: () => void;
}

interface DesktopStoreState {
    desktopState: DesktopLayoutState;
    navigate: null | ((args: { search: unknown; replace?: boolean }) => void);
    setNavigate: (
        navigate: (args: { search: unknown; replace?: boolean }) => void,
    ) => void;
    openWindow: (
        appId: string,
        options?: {
            instanceId?: string;
            title?: string;
            icon?: string;
            props?: Record<string, unknown>;
        },
    ) => void;
    focusWindow: (id: string) => void;
    closeWindow: (id: string) => void;
    minimizeWindow: (id: string) => void;
    restoreWindow: (id: string) => void;
    toggleMaximize: (id: string) => void;
    updateSize: (id: string, size: { width: number; height: number }) => void;
    updatePosition: (id: string, position: { x: number; y: number }) => void;
    setWindowProps: (
        id: string,
        update: (prev: Record<string, unknown>) => Record<string, unknown>,
        opts?: { replace?: boolean },
    ) => void;
    toggleStartMenu: () => void;
    closeStartMenu: () => void;
}

const findPreset = (id: string): WindowDescriptor | undefined =>
    WINDOW_PRESETS.find((preset) => preset.id === id);

export const useDesktopStore = create<DesktopStoreState>((set, get) => {
    const applyDesktopUpdate = (
        update: (prev: DesktopLayoutState) => DesktopLayoutState,
        opts?: { replace?: boolean },
    ) => {
        const prev = get().desktopState;
        const next = update(prev);

        if (next === prev) {
            return;
        }

        const navigate = get().navigate;
        if (navigate) {
            navigate({ search: () => next, replace: opts?.replace });
        }

        set({ desktopState: next });
    };

    return {
        desktopState: {
            windows: {},
            order: [],
            focusedId: null,
            zCounter: 0,
            startMenuOpen: false,
        },
        navigate: null,
        setNavigate: (navigate) => set({ navigate }),
        openWindow: (appId, options) => {
            const preset = findPreset(appId);
            if (!preset) {
                return;
            }

            const runtimeId = options?.instanceId ?? appId;

            applyDesktopUpdate((prev) => {
                if (prev.windows[runtimeId]) {
                    if (!useNavHistoryStore.getState().isNavigating) {
                        useNavHistoryStore
                            .getState()
                            .push({ kind: 'window', windowId: runtimeId });
                    }
                    const nextZ = prev.zCounter + 1;
                    return {
                        ...prev,
                        windows: {
                            ...prev.windows,
                            [runtimeId]: {
                                ...prev.windows[runtimeId],
                                zIndex: nextZ,
                                minimized: false,
                            },
                        },
                        order: prev.order
                            .filter((entry) => entry !== runtimeId)
                            .concat(runtimeId),
                        focusedId: runtimeId,
                        zCounter: nextZ,
                        startMenuOpen: false,
                    };
                }

                if (!useNavHistoryStore.getState().isNavigating) {
                    useNavHistoryStore
                        .getState()
                        .push({ kind: 'window', windowId: runtimeId });
                }

                const nextZ = prev.zCounter + 1;
                const existing = prev.windows[runtimeId] as
                    | DesktopWindowState
                    | undefined;
                const newWindow: DesktopWindowState = {
                    id: runtimeId,
                    appId,
                    title: options?.title ?? preset.title,
                    icon: options?.icon ?? preset.icon,
                    x: existing?.x ?? preset.initialPosition?.x ?? 160,
                    y: existing?.y ?? preset.initialPosition?.y ?? 120,
                    width: existing?.width ?? 720,
                    height: existing?.height ?? 480,
                    zIndex: nextZ,
                    minimized: false,
                    maximized: false,
                    restoreBounds: null,
                    props: options?.props ?? {},
                };

                return {
                    ...prev,
                    windows: { ...prev.windows, [runtimeId]: newWindow },
                    order: prev.order.concat(runtimeId),
                    focusedId: runtimeId,
                    zCounter: nextZ,
                    startMenuOpen: false,
                };
            });
        },
        focusWindow: (id: string) => {
            applyDesktopUpdate((prev) => {
                const w = prev.windows[id];
                if (!w) {
                    return prev;
                }

                if (
                    prev.focusedId !== id &&
                    !useNavHistoryStore.getState().isNavigating
                ) {
                    useNavHistoryStore
                        .getState()
                        .push({ kind: 'window', windowId: id });
                }

                const nextZ = prev.zCounter + 1;
                return {
                    ...prev,
                    windows: {
                        ...prev.windows,
                        [id]: {
                            ...w,
                            zIndex: nextZ,
                            minimized: false,
                        },
                    },
                    focusedId: id,
                    zCounter: nextZ,
                    order: prev.order
                        .filter((entry) => entry !== id)
                        .concat(id),
                };
            });
        },
        closeWindow: (id: string) => {
            applyDesktopUpdate((prev) => {
                if (!prev.windows[id]) {
                    return prev;
                }

                const { [id]: _omit, ...rest } = prev.windows;
                const newOrder = prev.order.filter((entry) => entry !== id);
                const newFocused =
                    prev.focusedId === id
                        ? (newOrder.at(-1) ?? null)
                        : prev.focusedId;

                return {
                    ...prev,
                    windows: rest,
                    order: newOrder,
                    focusedId: newFocused,
                    zCounter: prev.zCounter,
                };
            });
        },
        minimizeWindow: (id: string) => {
            applyDesktopUpdate((prev) => {
                const w = prev.windows[id];
                if (!w || w.minimized) return prev;

                const remaining = prev.order.filter(
                    (entry) => entry !== id && !prev.windows[entry]?.minimized,
                );
                const newFocused =
                    prev.focusedId === id
                        ? (remaining.at(-1) ?? null)
                        : prev.focusedId;

                return {
                    ...prev,
                    windows: {
                        ...prev.windows,
                        [id]: { ...w, minimized: true },
                    },
                    focusedId: newFocused,
                };
            });
        },
        restoreWindow: (id: string) => {
            applyDesktopUpdate((prev) => {
                const w = prev.windows[id];
                if (!w) return prev;
                const nextZ = prev.zCounter + 1;
                return {
                    ...prev,
                    windows: {
                        ...prev.windows,
                        [id]: {
                            ...w,
                            minimized: false,
                            zIndex: nextZ,
                        },
                    },
                    focusedId: id,
                    zCounter: nextZ,
                    order: prev.order
                        .filter((entry) => entry !== id)
                        .concat(id),
                };
            });
        },
        toggleMaximize: (id: string) => {
            applyDesktopUpdate((prev) => {
                const w = prev.windows[id];
                if (!w) return prev;
                const nextZ = prev.zCounter + 1;

                if (w.maximized) {
                    const r = w.restoreBounds;
                    return {
                        ...prev,
                        windows: {
                            ...prev.windows,
                            [id]: {
                                ...w,
                                maximized: false,
                                restoreBounds: null,
                                x: r?.x ?? w.x,
                                y: r?.y ?? w.y,
                                width: r?.width ?? w.width,
                                height: r?.height ?? w.height,
                                zIndex: nextZ,
                            },
                        },
                        focusedId: id,
                        zCounter: nextZ,
                        order: prev.order
                            .filter((entry) => entry !== id)
                            .concat(id),
                    };
                }

                return {
                    ...prev,
                    windows: {
                        ...prev.windows,
                        [id]: {
                            ...w,
                            maximized: true,
                            restoreBounds: {
                                x: w.x,
                                y: w.y,
                                width: w.width,
                                height: w.height,
                            },
                            zIndex: nextZ,
                        },
                    },
                    focusedId: id,
                    zCounter: nextZ,
                    order: prev.order
                        .filter((entry) => entry !== id)
                        .concat(id),
                };
            });
        },
        updateSize: (id, size) => {
            applyDesktopUpdate((prev) => {
                const currentWindow = prev.windows[id];

                if (
                    !currentWindow ||
                    (currentWindow.width === size.width &&
                        currentWindow.height === size.height)
                ) {
                    return prev;
                }

                return {
                    ...prev,
                    windows: {
                        ...prev.windows,
                        [id]: {
                            ...currentWindow,
                            ...size,
                        },
                    },
                    focusedId: id,
                    zCounter: prev.zCounter + 1,
                };
            });
        },
        updatePosition: (id, position) => {
            applyDesktopUpdate((prev) => {
                const currentWindow = prev.windows[id];
                if (!currentWindow) {
                    return prev;
                }

                if (
                    currentWindow.x === position.x &&
                    currentWindow.y === position.y
                ) {
                    return prev;
                }

                return {
                    ...prev,
                    windows: {
                        ...prev.windows,
                        [id]: {
                            ...currentWindow,
                            ...position,
                        },
                    },
                    focusedId: id,
                    zCounter: prev.zCounter + 1,
                };
            });
        },
        setWindowProps: (id, update, opts) => {
            applyDesktopUpdate((prev) => {
                const w = prev.windows[id];
                if (!w) return prev;
                const prevProps = w.props ?? {};
                const nextProps = update(prevProps);
                if (nextProps === prevProps) return prev;
                return {
                    ...prev,
                    windows: {
                        ...prev.windows,
                        [id]: { ...w, props: nextProps },
                    },
                };
            }, opts);
        },
        toggleStartMenu: () => {
            applyDesktopUpdate((prev) => ({
                ...prev,
                startMenuOpen: !prev.startMenuOpen,
            }));
        },
        closeStartMenu: () => {
            applyDesktopUpdate((prev) =>
                prev.startMenuOpen ? { ...prev, startMenuOpen: false } : prev,
            );
        },
    };
});

export function DesktopProvider({ children }: { children: ReactNode }) {
    const navigate = useNavigate();

    useEffect(() => {
        useDesktopStore
            .getState()
            .setNavigate(
                navigate as (args: {
                    search: unknown;
                    replace?: boolean;
                }) => void,
            );
    }, [navigate]);

    return <>{children}</>;
}

export function useDesktopContext(): DesktopContextInternal {
    const desktopState = useDesktopStore((state) => state.desktopState);
    const openWindow = useDesktopStore((state) => state.openWindow);
    const focusWindow = useDesktopStore((state) => state.focusWindow);
    const closeWindow = useDesktopStore((state) => state.closeWindow);
    const minimizeWindow = useDesktopStore((state) => state.minimizeWindow);
    const restoreWindow = useDesktopStore((state) => state.restoreWindow);
    const toggleMaximize = useDesktopStore((state) => state.toggleMaximize);
    const updateSize = useDesktopStore((state) => state.updateSize);
    const updatePosition = useDesktopStore((state) => state.updatePosition);
    const setWindowProps = useDesktopStore((state) => state.setWindowProps);
    const toggleStartMenu = useDesktopStore((state) => state.toggleStartMenu);
    const closeStartMenu = useDesktopStore((state) => state.closeStartMenu);

    return {
        desktopState,
        openWindow,
        focusWindow,
        closeWindow,
        minimizeWindow,
        restoreWindow,
        toggleMaximize,
        updateSize,
        updatePosition,
        setWindowProps,
        toggleStartMenu,
        closeStartMenu,
    };
}
