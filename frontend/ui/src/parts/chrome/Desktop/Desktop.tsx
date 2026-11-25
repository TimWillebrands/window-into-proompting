import {
    type ComponentPropsWithRef,
    createContext,
    memo,
    type ReactNode,
    type PointerEvent as ReactPointerEvent,
    type RefObject,
    useCallback,
    useContext,
    useLayoutEffect,
    useRef,
    useState,
} from 'react';
import { flushSync } from 'react-dom';
import { type SkinName, skinOf } from '../../../../skins';
import { Icon, type IconName, type IconSize, iconNames } from '../../icons';
import { ContextMenu, useContextMenu } from '../ContextMenu/ContextMenu';
import type { MenuEntry } from '../ContextMenu/menu';
import { StartButton } from '../StartButton/StartButton';
import { Taskbar, TaskbarButton } from '../Taskbar/Taskbar';
import { morph, taskbarButtonElement, windowElement } from '../transitions';
import { Window } from '../Window/Window';
import {
    clampBounds,
    type ManagedWindow,
    type SnapEdge,
    snapBounds,
    type WindowAction,
    type WindowBounds,
    type WindowManagerState,
    type WindowSize,
    windowActions,
    windowReducer,
} from '../WindowManager/WindowManager';

export type ProgramWindow = { id: string; title: string };

export type Program = {
    id: string;
    title: string;
    icon: string;
    render: (
        props: Record<string, unknown>,
        window: ProgramWindow,
    ) => ReactNode;
    defaultBounds?: Partial<WindowBounds>;
    minSize?: WindowSize;
    singleton?: boolean;
    skin?: SkinName;
};

export type LaunchOptions = {
    id?: string;
    title?: string;
    icon?: string;
    props?: Record<string, unknown>;
    area?: WindowSize;
};

export type DesktopProps = Omit<ComponentPropsWithRef<'div'>, 'children'> & {
    state: WindowManagerState;
    onAction: (action: WindowAction) => void;
    programs: readonly Program[];
    renderIcon?: (icon: string, size: IconSize) => ReactNode;
    wallpaper?: ReactNode;
    icons?: ReactNode;
    start?: ReactNode;
    startMenu?: ReactNode;
    tray?: ReactNode;
    desktopMenu?: MenuEntry[];
    minSize?: WindowSize;
    children?: ReactNode;
};

type Grip = 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw';

const grips: readonly Grip[] = ['n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw'];

const morphName = 'ui-window-morph';
const dragThreshold = 4;
const snapReach = 6;
const defaultMinSize: WindowSize = { width: 360, height: 240 };

export function measureDesktopArea(): WindowSize | undefined {
    if (typeof document === 'undefined') return undefined;
    const area = document.querySelector('.ui-desktop-area');
    if (!area) return undefined;
    return { width: area.clientWidth, height: area.clientHeight };
}

export function launchProgram(
    program: Program,
    options: LaunchOptions = {},
): WindowAction {
    return windowActions.open({
        appId: program.id,
        id: options.id,
        title: options.title ?? program.title,
        icon: options.icon ?? program.icon,
        singleton: program.singleton,
        bounds: program.defaultBounds,
        props: options.props,
        area: options.area ?? measureDesktopArea(),
    });
}

const MinimisedContext = createContext(false);

export const useWindowMinimised = () => useContext(MinimisedContext);

const canMaximise = (program: Program | undefined) =>
    skinOf(program?.skin)?.maximise !== false;

const isIconName = (icon: string): icon is IconName =>
    (iconNames as readonly string[]).includes(icon);

const iconOf = (icon: string, size: IconSize) => (
    <Icon name={isIconName(icon) ? icon : 'document'} size={size} />
);

const focusWindowElement = (id: string | null) => {
    if (!id) return;
    requestAnimationFrame(() => {
        const element = windowElement(id);
        if (element instanceof HTMLElement)
            element.focus({ preventScroll: true });
    });
};

const edgeAt = (x: number, y: number, area: WindowSize): SnapEdge | null => {
    if (y <= snapReach) return 'top';
    if (x <= snapReach) return 'left';
    if (x >= area.width - snapReach) return 'right';
    return null;
};

function useArea(ref: RefObject<HTMLDivElement | null>) {
    const [area, setArea] = useState<WindowSize>();
    useLayoutEffect(() => {
        const element = ref.current;
        if (!element) return;
        const update = () =>
            setArea((previous) =>
                previous?.width === element.clientWidth &&
                previous.height === element.clientHeight
                    ? previous
                    : {
                          width: element.clientWidth,
                          height: element.clientHeight,
                      },
            );
        update();
        const observer = new ResizeObserver(update);
        observer.observe(element);
        return () => observer.disconnect();
    }, [ref]);
    return area;
}

type Gesture = {
    step: (dx: number, dy: number, pointer: PointerEvent) => WindowBounds;
    finish: (bounds: WindowBounds, pointer: PointerEvent) => void;
    cancel?: () => void;
};

function track(
    event: ReactPointerEvent<HTMLElement>,
    frame: HTMLElement,
    start: WindowBounds,
    gesture: Gesture,
) {
    const target = event.target instanceof Element ? event.target : null;
    const { pointerId, clientX, clientY } = event;
    let moved = false;
    let latest = start;
    let raf = 0;
    const paint = (bounds: WindowBounds) => {
        frame.style.transform = `translate(${bounds.x}px, ${bounds.y}px)`;
        frame.style.width = `${bounds.width}px`;
        frame.style.height = `${bounds.height}px`;
    };
    const onMove = (pointer: PointerEvent) => {
        if (pointer.pointerId !== pointerId) return;
        const dx = pointer.clientX - clientX;
        const dy = pointer.clientY - clientY;
        if (!moved && Math.abs(dx) + Math.abs(dy) < dragThreshold) return;
        if (!moved) {
            moved = true;
            frame.setAttribute('data-moving', '');
            try {
                target?.setPointerCapture(pointerId);
            } catch {}
        }
        latest = gesture.step(dx, dy, pointer);
        if (!raf)
            raf = requestAnimationFrame(() => {
                raf = 0;
                paint(latest);
            });
    };
    const stop = () => {
        window.removeEventListener('pointermove', onMove);
        window.removeEventListener('pointerup', onUp);
        window.removeEventListener('pointercancel', onCancel);
        window.removeEventListener('keydown', onKey, true);
        cancelAnimationFrame(raf);
        if (target?.hasPointerCapture(pointerId))
            target.releasePointerCapture(pointerId);
        frame.removeAttribute('data-moving');
        if (moved) paint(start);
        return moved;
    };
    const onUp = (pointer: PointerEvent) => {
        if (pointer.pointerId !== pointerId) return;
        if (stop()) flushSync(() => gesture.finish(latest, pointer));
    };
    const onCancel = () => {
        stop();
        gesture.cancel?.();
    };
    const onKey = (key: KeyboardEvent) => {
        if (key.key !== 'Escape' || !moved) return;
        key.preventDefault();
        key.stopPropagation();
        onCancel();
    };
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    window.addEventListener('pointercancel', onCancel);
    window.addEventListener('keydown', onKey, true);
}

function resized(
    grip: Grip,
    start: WindowBounds,
    dx: number,
    dy: number,
    min: WindowSize,
    area: WindowSize | undefined,
): WindowBounds {
    let { x, y, width, height } = start;
    if (grip.includes('e')) width = Math.max(min.width, start.width + dx);
    if (grip.includes('s')) height = Math.max(min.height, start.height + dy);
    if (grip.includes('w')) {
        width = Math.max(min.width, start.width - dx);
        x = start.x + start.width - width;
    }
    if (grip.includes('n')) {
        height = Math.max(min.height, start.height - dy);
        y = start.y + start.height - height;
    }
    if (area) {
        if (x < 0) {
            width += x;
            x = 0;
        }
        if (y < 0) {
            height += y;
            y = 0;
        }
        width = Math.min(width, area.width - x);
        height = Math.min(height, area.height - y);
    }
    return { x, y, width, height };
}

const ProgramContent = memo(function ProgramContent({
    program,
    id,
    title,
    props,
}: {
    program: Program;
    id: string;
    title: string;
    props: Record<string, unknown>;
}) {
    return program.render(props, { id, title });
});

type DesktopWindowProps = {
    entry: ManagedWindow;
    program: Program;
    focused: boolean;
    area: WindowSize | undefined;
    minSize: WindowSize;
    renderIcon: (icon: string, size: IconSize) => ReactNode;
    onAction: (action: WindowAction) => void;
    onMinimise: (id: string) => void;
    onClose: (id: string) => void;
    onFocusIn: (id: string) => void;
    onSnapZone: (id: string, edge: SnapEdge | null) => void;
};

function DesktopWindow({
    entry,
    program,
    focused,
    area,
    minSize,
    renderIcon,
    onAction,
    onMinimise,
    onClose,
    onFocusIn,
    onSnapZone,
}: DesktopWindowProps) {
    const frame = useRef<HTMLDivElement>(null);
    const look = skinOf(program.skin);
    const maximisable = canMaximise(program);
    const maximised = entry.maximized && maximisable;
    const shown = maximised ? null : clampBounds(entry, area);
    const floor = program.minSize ?? look?.minSize ?? minSize;
    const min = {
        width: Math.min(floor.width, area?.width ?? Infinity),
        height: Math.min(floor.height, area?.height ?? Infinity),
    };

    const startMove = (event: ReactPointerEvent<HTMLElement>) => {
        if (event.button !== 0 || !shown || !frame.current) return;
        const bar = (event.target as Element).closest('.ui-window-titlebar');
        if (!bar || bar.parentElement !== event.currentTarget) return;
        const box = frame.current.parentElement?.getBoundingClientRect();
        const left = box?.left ?? 0;
        const top = box?.top ?? 0;
        const size = entry.restoreBounds ?? shown;
        const grabbed = event.clientX - left - shown.x;
        const grab = entry.restoreBounds
            ? Math.min(size.width, (grabbed / shown.width) * size.width)
            : grabbed;
        const edge = (pointer: PointerEvent) => {
            const reached = area
                ? edgeAt(pointer.clientX - left, pointer.clientY - top, area)
                : null;
            return reached === 'top' && !maximisable ? null : reached;
        };
        track(event, frame.current, shown, {
            step: (_dx, dy, pointer) => {
                onSnapZone(entry.id, edge(pointer));
                return clampBounds(
                    {
                        x: pointer.clientX - left - grab,
                        y: shown.y + dy,
                        width: size.width,
                        height: size.height,
                    },
                    area,
                );
            },
            finish: (bounds, pointer) => {
                onSnapZone(entry.id, null);
                const snap = edge(pointer);
                if (snap && area)
                    onAction(windowActions.snap(entry.id, snap, area));
                else
                    onAction(
                        windowActions.move(
                            entry.id,
                            { x: bounds.x, y: bounds.y },
                            area,
                        ),
                    );
            },
            cancel: () => onSnapZone(entry.id, null),
        });
    };

    const startResize =
        (grip: Grip) => (event: ReactPointerEvent<HTMLElement>) => {
            if (event.button !== 0 || !shown || !frame.current) return;
            event.preventDefault();
            track(event, frame.current, shown, {
                step: (dx, dy) => resized(grip, shown, dx, dy, min, area),
                finish: (bounds) =>
                    onAction(
                        windowActions.resize(entry.id, bounds, {
                            minSize: min,
                            area,
                        }),
                    ),
            });
        };

    return (
        // biome-ignore lint/a11y/noStaticElementInteractions: a press anywhere in a window brings it to the front, as on any desktop
        <div
            ref={frame}
            className="ui-desktop-window"
            data-maximised={maximised ? '' : undefined}
            data-minimised={entry.minimized ? '' : undefined}
            inert={entry.minimized || undefined}
            style={
                shown
                    ? {
                          zIndex: entry.zIndex,
                          width: shown.width,
                          height: shown.height,
                          transform: `translate(${shown.x}px, ${shown.y}px)`,
                      }
                    : { zIndex: entry.zIndex }
            }
            onPointerDownCapture={() => onAction(windowActions.focus(entry.id))}
            onFocus={() => onFocusIn(entry.id)}
        >
            <Window
                windowId={entry.id}
                title={entry.title}
                icon={renderIcon(
                    entry.icon,
                    look?.titleBar === 'tall' ? 32 : 16,
                )}
                skin={program.skin}
                active={focused}
                maximised={maximised}
                tabIndex={-1}
                onPointerDown={startMove}
                onMinimise={() => onMinimise(entry.id)}
                onMaximise={() =>
                    onAction(windowActions.toggleMaximise(entry.id))
                }
                onClose={() => onClose(entry.id)}
            >
                <MinimisedContext.Provider value={entry.minimized}>
                    <ProgramContent
                        program={program}
                        id={entry.id}
                        title={entry.title}
                        props={entry.props}
                    />
                </MinimisedContext.Provider>
            </Window>
            {shown &&
                grips.map((grip) => (
                    <span
                        key={grip}
                        className="ui-desktop-grip"
                        data-grip={grip}
                        aria-hidden="true"
                        onPointerDown={startResize(grip)}
                    />
                ))}
        </div>
    );
}

export function Desktop({
    state,
    onAction,
    programs,
    renderIcon = iconOf,
    wallpaper,
    icons,
    start,
    startMenu,
    tray,
    desktopMenu,
    minSize = defaultMinSize,
    className,
    children,
    ...rest
}: DesktopProps) {
    const areaRef = useRef<HTMLDivElement>(null);
    const iconsRef = useRef<HTMLDivElement>(null);
    const area = useArea(areaRef);
    const menu = useContextMenu();
    const [snap, setSnap] = useState<{ id: string; edge: SnapEdge } | null>(
        null,
    );
    const latest = useRef(state);
    latest.current = state;
    const windows = Object.values(state.windows);
    const programOf = (entry: ManagedWindow) =>
        programs.find((program) => program.id === (entry.appId || entry.id));

    const onSnapZone = useCallback(
        (id: string, edge: SnapEdge | null) =>
            setSnap((previous) =>
                !edge
                    ? null
                    : previous?.id === id && previous.edge === edge
                      ? previous
                      : { id, edge },
            ),
        [],
    );

    const nextFocus = (action: WindowAction) =>
        windowReducer(latest.current, action).focusedId;

    const minimise = (id: string, moveFocus: boolean) => {
        const action = windowActions.minimise(id);
        const next = nextFocus(action);
        void morph({
            from: windowElement(id),
            to: () => taskbarButtonElement(id),
            update: () => onAction(action),
            name: morphName,
        }).then(() => {
            if (moveFocus) focusWindowElement(next);
        });
    };

    const unminimise = (id: string, action: WindowAction) => {
        void morph({
            from: taskbarButtonElement(id),
            to: () => windowElement(id),
            update: () => onAction(action),
            name: morphName,
        }).then(() => focusWindowElement(id));
    };

    const close = (id: string) => {
        const action = windowActions.close(id);
        const next = nextFocus(action);
        onAction(action);
        focusWindowElement(next);
    };

    const activate = (entry: ManagedWindow) => {
        if (entry.minimized)
            unminimise(entry.id, windowActions.restore(entry.id));
        else if (latest.current.focusedId === entry.id)
            minimise(entry.id, false);
        else {
            onAction(windowActions.focus(entry.id));
            focusWindowElement(entry.id);
        }
    };

    const onFocusIn = (id: string) => {
        if (latest.current.focusedId !== id) onAction(windowActions.focus(id));
    };

    const snapping = snap && area ? state.windows[snap.id] : undefined;
    const preview =
        snap && area && snapping
            ? {
                  edge: snap.edge,
                  zIndex: snapping.zIndex,
                  ...snapBounds(snap.edge, area),
              }
            : null;

    return (
        <div
            className={className ? `ui-desktop ${className}` : 'ui-desktop'}
            {...rest}
        >
            {wallpaper && (
                <div className="ui-desktop-wallpaper">{wallpaper}</div>
            )}
            {/* biome-ignore lint/a11y/noStaticElementInteractions: right-clicking the bare desktop opens its menu, as on XP; its items are reachable elsewhere too */}
            <div
                ref={areaRef}
                className="ui-desktop-area"
                onContextMenu={(event) => {
                    const bare =
                        event.target === areaRef.current ||
                        event.target === iconsRef.current;
                    if (desktopMenu && bare) menu.onContextMenu(event);
                }}
            >
                {icons && (
                    <div ref={iconsRef} className="ui-desktop-icons">
                        {icons}
                    </div>
                )}
                {preview && (
                    <div
                        className="ui-desktop-snap"
                        data-edge={preview.edge}
                        aria-hidden="true"
                        style={{
                            zIndex: preview.zIndex,
                            width: preview.width,
                            height: preview.height,
                            transform: `translate(${preview.x}px, ${preview.y}px)`,
                        }}
                    />
                )}
                {windows.map((entry) => {
                    const program = programOf(entry);
                    if (!program) return null;
                    return (
                        <DesktopWindow
                            key={entry.id}
                            entry={entry}
                            program={program}
                            focused={state.focusedId === entry.id}
                            area={area}
                            minSize={minSize}
                            renderIcon={renderIcon}
                            onAction={onAction}
                            onMinimise={(id) => minimise(id, true)}
                            onClose={close}
                            onFocusIn={onFocusIn}
                            onSnapZone={onSnapZone}
                        />
                    );
                })}
            </div>
            <Taskbar
                className="ui-desktop-taskbar"
                start={
                    start ?? (
                        <StartButton
                            open={state.startMenuOpen}
                            title="Start"
                            onClick={() => onAction(windowActions.startMenu())}
                        />
                    )
                }
                tray={tray}
            >
                {windows.map((entry) => {
                    const maximisable = canMaximise(programOf(entry));
                    return (
                        <TaskbarButton
                            key={entry.id}
                            taskId={entry.id}
                            label={entry.title}
                            title={entry.title}
                            icon={renderIcon(entry.icon, 16)}
                            active={
                                !entry.minimized && state.focusedId === entry.id
                            }
                            minimised={entry.minimized}
                            maximised={maximisable && entry.maximized}
                            onClick={() => activate(entry)}
                            onRestore={() =>
                                entry.minimized
                                    ? unminimise(
                                          entry.id,
                                          windowActions.restore(entry.id),
                                      )
                                    : onAction(windowActions.restore(entry.id))
                            }
                            onMinimise={() => minimise(entry.id, false)}
                            onMaximise={
                                maximisable
                                    ? () =>
                                          entry.minimized
                                              ? unminimise(
                                                    entry.id,
                                                    windowActions.maximise(
                                                        entry.id,
                                                    ),
                                                )
                                              : onAction(
                                                    windowActions.maximise(
                                                        entry.id,
                                                    ),
                                                )
                                    : undefined
                            }
                            onClose={() =>
                                onAction(windowActions.close(entry.id))
                            }
                        />
                    );
                })}
            </Taskbar>
            {desktopMenu && (
                <ContextMenu
                    {...menu.menuProps}
                    items={desktopMenu}
                    label="Desktop"
                />
            )}
            {state.startMenuOpen && startMenu && (
                <div className="ui-desktop-start-menu">{startMenu}</div>
            )}
            {children}
        </div>
    );
}
