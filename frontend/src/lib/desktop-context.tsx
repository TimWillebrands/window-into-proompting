import { useNavigate } from '@tanstack/react-router';
import { useEffect, type ReactNode } from 'react';
import { create } from 'zustand';
import type { DesktopLayoutState } from '../types/desktop';
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
    updateSize: (id: string, size: { width: number; height: number }) => void;
    updatePosition: (id: string, position: { x: number; y: number }) => void;
}

interface DesktopStoreState {
    desktopState: DesktopLayoutState;
    navigate: null | ((args: { search: unknown }) => void);
    setNavigate: (navigate: (args: { search: unknown }) => void) => void;
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
    updateSize: (id: string, size: { width: number; height: number }) => void;
    updatePosition: (id: string, position: { x: number; y: number }) => void;
}

const findPreset = (id: string): WindowDescriptor | undefined =>
    WINDOW_PRESETS.find((preset) => preset.id === id);

export const useDesktopStore = create<DesktopStoreState>((set, get) => {
    const applyDesktopUpdate = (
        update: (prev: DesktopLayoutState) => DesktopLayoutState,
    ) => {
        const prev = get().desktopState;
        const next = update(prev);

        if (next === prev) {
            return;
        }

        const navigate = get().navigate;
        if (navigate) {
            navigate({ search: () => next });
        }

        set({ desktopState: next });
    };

    return {
        desktopState: {
            windows: {},
            order: [],
            focusedId: null,
            zCounter: 0,
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
                    const nextZ = prev.zCounter + 1;
                    return {
                        windows: {
                            ...prev.windows,
                            [runtimeId]: {
                                ...prev.windows[runtimeId],
                                zIndex: nextZ,
                            },
                        },
                        order: prev.order
                            .filter((entry) => entry !== runtimeId)
                            .concat(runtimeId),
                        focusedId: runtimeId,
                        zCounter: nextZ,
                    };
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
                    props: options?.props ?? {},
                };

                return {
                    windows: { ...prev.windows, [runtimeId]: newWindow },
                    order: prev.order.concat(runtimeId),
                    focusedId: runtimeId,
                    zCounter: nextZ,
                };
            });
        },
        focusWindow: (id: string) => {
            applyDesktopUpdate((prev) => {
                if (!prev.windows[id]) {
                    return prev;
                }

                const nextZ = prev.zCounter + 1;
                return {
                    ...prev,
                    windows: {
                        ...prev.windows,
                        [id]: {
                            ...prev.windows[id],
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
                    windows: rest,
                    order: newOrder,
                    focusedId: newFocused,
                    zCounter: prev.zCounter,
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
    };
});

export function DesktopProvider({ children }: { children: ReactNode }) {
    const navigate = useNavigate();

    useEffect(() => {
        useDesktopStore
            .getState()
            .setNavigate(navigate as (args: { search: unknown }) => void);
    }, [navigate]);

    return <>{children}</>;
}

export function useDesktopContext(): DesktopContextInternal {
    const desktopState = useDesktopStore((state) => state.desktopState);
    const openWindow = useDesktopStore((state) => state.openWindow);
    const focusWindow = useDesktopStore((state) => state.focusWindow);
    const closeWindow = useDesktopStore((state) => state.closeWindow);
    const updateSize = useDesktopStore((state) => state.updateSize);
    const updatePosition = useDesktopStore((state) => state.updatePosition);

    return {
        desktopState,
        openWindow,
        focusWindow,
        closeWindow,
        updateSize,
        updatePosition,
    };
}
