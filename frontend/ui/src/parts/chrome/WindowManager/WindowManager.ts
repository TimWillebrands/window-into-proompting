export type WindowBounds = {
    x: number;
    y: number;
    width: number;
    height: number;
};

export type WindowSize = { width: number; height: number };

export type ManagedWindow = WindowBounds & {
    id: string;
    appId: string;
    title: string;
    icon: string;
    zIndex: number;
    minimized: boolean;
    maximized: boolean;
    restoreBounds: WindowBounds | null;
    props: Record<string, unknown>;
};

export type WindowManagerState = {
    windows: Record<string, ManagedWindow>;
    order: string[];
    focusedId: string | null;
    zCounter: number;
    startMenuOpen: boolean;
};

export type SnapEdge = 'left' | 'right' | 'top';

export type OpenWindow = {
    appId: string;
    title: string;
    icon: string;
    id?: string;
    singleton?: boolean;
    bounds?: Partial<WindowBounds>;
    props?: Record<string, unknown>;
    area?: WindowSize;
};

export type WindowAction =
    | ({ type: 'open' } & OpenWindow)
    | { type: 'focus'; id: string }
    | { type: 'close'; id: string }
    | { type: 'minimise'; id: string }
    | { type: 'restore'; id: string }
    | { type: 'maximise'; id: string }
    | { type: 'toggleMaximise'; id: string }
    | { type: 'move'; id: string; x: number; y: number; area?: WindowSize }
    | {
          type: 'resize';
          id: string;
          bounds: WindowBounds;
          minSize?: WindowSize;
          area?: WindowSize;
      }
    | { type: 'snap'; id: string; edge: SnapEdge; area: WindowSize }
    | { type: 'setProps'; id: string; props: Record<string, unknown> }
    | { type: 'startMenu'; open?: boolean };

export const cascadeStep = 24;

const defaultSize: WindowSize = { width: 640, height: 480 };

export const emptyDesktop: WindowManagerState = {
    windows: {},
    order: [],
    focusedId: null,
    zCounter: 0,
    startMenuOpen: false,
};

const clamp = (value: number, min: number, max: number) =>
    Math.min(Math.max(value, min), Math.max(min, max));

export function clampBounds(
    bounds: WindowBounds,
    area?: WindowSize,
): WindowBounds {
    if (!area) return bounds;
    const width = Math.min(bounds.width, area.width);
    const height = Math.min(bounds.height, area.height);
    return {
        x: clamp(bounds.x, 0, area.width - width),
        y: clamp(bounds.y, 0, area.height - height),
        width,
        height,
    };
}

export function snapBounds(edge: SnapEdge, area: WindowSize): WindowBounds {
    if (edge === 'top')
        return { x: 0, y: 0, width: area.width, height: area.height };
    const half = Math.round(area.width / 2);
    return edge === 'left'
        ? { x: 0, y: 0, width: half, height: area.height }
        : { x: half, y: 0, width: area.width - half, height: area.height };
}

const boundsOf = (window: ManagedWindow): WindowBounds => ({
    x: window.x,
    y: window.y,
    width: window.width,
    height: window.height,
});

const sameBounds = (a: WindowBounds, b: WindowBounds) =>
    a.x === b.x && a.y === b.y && a.width === b.width && a.height === b.height;

export function topWindow(
    state: WindowManagerState,
    except?: string,
): ManagedWindow | undefined {
    let top: ManagedWindow | undefined;
    for (const window of Object.values(state.windows)) {
        if (window.id === except || window.minimized) continue;
        if (!top || window.zIndex > top.zIndex) top = window;
    }
    return top;
}

function cascade(
    state: WindowManagerState,
    size: WindowSize,
    preferred: Partial<WindowBounds> | undefined,
    area: WindowSize | undefined,
) {
    const last = topWindow(state);
    let x = last ? last.x + cascadeStep : (preferred?.x ?? cascadeStep * 2);
    let y = last ? last.y + cascadeStep : (preferred?.y ?? cascadeStep);
    if (area && last) {
        if (y + size.height > area.height) y = cascadeStep;
        if (x + size.width > area.width) x = cascadeStep;
    }
    return clampBounds({ x, y, ...size }, area);
}

function freshId(state: WindowManagerState, appId: string) {
    let n = state.zCounter + 1;
    while (state.windows[`${appId}-${n}`]) n += 1;
    return `${appId}-${n}`;
}

function raise(
    state: WindowManagerState,
    id: string,
    patch: Partial<ManagedWindow> = {},
): WindowManagerState {
    const window = state.windows[id];
    if (!window) return state;
    const zIndex = state.zCounter + 1;
    return {
        ...state,
        windows: { ...state.windows, [id]: { ...window, ...patch, zIndex } },
        order: state.order.filter((entry) => entry !== id).concat(id),
        focusedId: id,
        zCounter: zIndex,
    };
}

const isFront = (state: WindowManagerState, window: ManagedWindow) =>
    state.focusedId === window.id &&
    !window.minimized &&
    window.zIndex === state.zCounter;

function patch(
    state: WindowManagerState,
    id: string,
    changes: Partial<ManagedWindow>,
): WindowManagerState {
    const window = state.windows[id];
    if (!window) return state;
    return {
        ...state,
        windows: { ...state.windows, [id]: { ...window, ...changes } },
    };
}

function open(state: WindowManagerState, action: OpenWindow) {
    const id =
        action.id ??
        (action.singleton === false
            ? freshId(state, action.appId)
            : action.appId);
    const existing = state.windows[id];
    const closed = { ...state, startMenuOpen: false };
    if (existing)
        return raise(closed, id, {
            minimized: false,
            props: action.props
                ? { ...existing.props, ...action.props }
                : existing.props,
        });
    const size = {
        width: action.bounds?.width ?? defaultSize.width,
        height: action.bounds?.height ?? defaultSize.height,
    };
    const bounds = cascade(state, size, action.bounds, action.area);
    const zIndex = state.zCounter + 1;
    const window: ManagedWindow = {
        id,
        appId: action.appId,
        title: action.title,
        icon: action.icon,
        ...bounds,
        zIndex,
        minimized: false,
        maximized: false,
        restoreBounds: null,
        props: action.props ?? {},
    };
    return {
        ...closed,
        windows: { ...state.windows, [id]: window },
        order: state.order.concat(id),
        focusedId: id,
        zCounter: zIndex,
    };
}

function close(state: WindowManagerState, id: string) {
    if (!state.windows[id]) return state;
    const { [id]: _closed, ...windows } = state.windows;
    const next = {
        ...state,
        windows,
        order: state.order.filter((entry) => entry !== id),
    };
    return {
        ...next,
        focusedId:
            state.focusedId === id
                ? (topWindow(next)?.id ?? null)
                : state.focusedId,
    };
}

function minimise(state: WindowManagerState, id: string) {
    const window = state.windows[id];
    if (!window || window.minimized) return state;
    const next = patch(state, id, { minimized: true });
    return {
        ...next,
        focusedId:
            state.focusedId === id
                ? (topWindow(next, id)?.id ?? null)
                : state.focusedId,
    };
}

function move(
    state: WindowManagerState,
    action: Extract<WindowAction, { type: 'move' }>,
) {
    const window = state.windows[action.id];
    if (!window || window.maximized) return state;
    const size = window.restoreBounds ?? window;
    const bounds = clampBounds(
        { x: action.x, y: action.y, width: size.width, height: size.height },
        action.area,
    );
    if (!window.restoreBounds && sameBounds(bounds, boundsOf(window)))
        return state;
    return patch(state, action.id, { ...bounds, restoreBounds: null });
}

function resize(
    state: WindowManagerState,
    action: Extract<WindowAction, { type: 'resize' }>,
) {
    const window = state.windows[action.id];
    if (!window || window.maximized) return state;
    const min = action.minSize ?? { width: 0, height: 0 };
    const bounds = clampBounds(
        {
            ...action.bounds,
            width: Math.max(action.bounds.width, min.width),
            height: Math.max(action.bounds.height, min.height),
        },
        action.area,
    );
    if (!window.restoreBounds && sameBounds(bounds, boundsOf(window)))
        return state;
    return patch(state, action.id, { ...bounds, restoreBounds: null });
}

function snap(
    state: WindowManagerState,
    action: Extract<WindowAction, { type: 'snap' }>,
) {
    const window = state.windows[action.id];
    if (!window) return state;
    if (action.edge === 'top')
        return raise(state, action.id, { maximized: true, minimized: false });
    return raise(state, action.id, {
        ...snapBounds(action.edge, action.area),
        maximized: false,
        minimized: false,
        restoreBounds: window.restoreBounds ?? boundsOf(window),
    });
}

export function windowReducer(
    state: WindowManagerState,
    action: WindowAction,
): WindowManagerState {
    switch (action.type) {
        case 'open':
            return open(state, action);
        case 'focus': {
            const window = state.windows[action.id];
            if (!window || isFront(state, window)) return state;
            return raise(state, action.id, { minimized: false });
        }
        case 'close':
            return close(state, action.id);
        case 'minimise':
            return minimise(state, action.id);
        case 'restore': {
            const window = state.windows[action.id];
            if (!window) return state;
            if (window.minimized)
                return raise(state, action.id, { minimized: false });
            if (window.maximized)
                return raise(state, action.id, { maximized: false });
            return isFront(state, window) ? state : raise(state, action.id);
        }
        case 'maximise': {
            const window = state.windows[action.id];
            if (!window) return state;
            if (window.maximized && isFront(state, window)) return state;
            return raise(state, action.id, {
                maximized: true,
                minimized: false,
            });
        }
        case 'toggleMaximise': {
            const window = state.windows[action.id];
            if (!window) return state;
            return raise(state, action.id, {
                maximized: !window.maximized,
                minimized: false,
            });
        }
        case 'move':
            return move(state, action);
        case 'resize':
            return resize(state, action);
        case 'snap':
            return snap(state, action);
        case 'setProps': {
            const window = state.windows[action.id];
            if (!window || window.props === action.props) return state;
            return patch(state, action.id, { props: action.props });
        }
        case 'startMenu': {
            const open = action.open ?? !state.startMenuOpen;
            return open === state.startMenuOpen
                ? state
                : { ...state, startMenuOpen: open };
        }
    }
}

export const windowActions = {
    open: (options: OpenWindow): WindowAction => ({ type: 'open', ...options }),
    focus: (id: string): WindowAction => ({ type: 'focus', id }),
    close: (id: string): WindowAction => ({ type: 'close', id }),
    minimise: (id: string): WindowAction => ({ type: 'minimise', id }),
    restore: (id: string): WindowAction => ({ type: 'restore', id }),
    maximise: (id: string): WindowAction => ({ type: 'maximise', id }),
    toggleMaximise: (id: string): WindowAction => ({
        type: 'toggleMaximise',
        id,
    }),
    move: (
        id: string,
        position: { x: number; y: number },
        area?: WindowSize,
    ): WindowAction => ({ type: 'move', id, ...position, area }),
    resize: (
        id: string,
        bounds: WindowBounds,
        options: { minSize?: WindowSize; area?: WindowSize } = {},
    ): WindowAction => ({ type: 'resize', id, bounds, ...options }),
    snap: (id: string, edge: SnapEdge, area: WindowSize): WindowAction => ({
        type: 'snap',
        id,
        edge,
        area,
    }),
    setProps: (id: string, props: Record<string, unknown>): WindowAction => ({
        type: 'setProps',
        id,
        props,
    }),
    startMenu: (open?: boolean): WindowAction => ({ type: 'startMenu', open }),
};
