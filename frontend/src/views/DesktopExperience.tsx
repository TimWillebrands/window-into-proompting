import { createElement, useEffect, useMemo, useState } from 'react';
import { Rnd } from 'react-rnd';
import DesktopIcon from '../components/desktop/DesktopIcon';
import DesktopTaskbar from '../components/desktop/DesktopTaskbar';
import DesktopWallpaper from '../components/desktop/DesktopWallpaper';
import StartMenu from '../components/desktop/StartMenu';
import WindowFrame from '../components/desktop/WindowFrame';
import { WindowInstanceProvider } from '../components/desktop/window-instance-context';
import { DesktopProvider, useDesktopContext } from '../lib/desktop-context';
import { DESKTOP_ICONS, WINDOW_PRESETS } from '../lib/window-presets';

const TASKBAR_HEIGHT = 30;
const MIN_WIDTH = 360;
const MIN_HEIGHT = 240;
const DRAG_HANDLE = 'window-drag-handle';

function useViewport() {
    const [size, setSize] = useState(() => ({
        width: typeof window === 'undefined' ? 1024 : window.innerWidth,
        height: typeof window === 'undefined' ? 768 : window.innerHeight,
    }));
    useEffect(() => {
        const handler = () =>
            setSize({ width: window.innerWidth, height: window.innerHeight });
        window.addEventListener('resize', handler);
        return () => window.removeEventListener('resize', handler);
    }, []);
    return size;
}

function DesktopWindowLayer() {
    const {
        desktopState,
        openWindow,
        focusWindow,
        closeWindow,
        minimizeWindow,
        restoreWindow,
        toggleMaximize,
        updateSize,
        updatePosition,
        toggleStartMenu,
        closeStartMenu,
    } = useDesktopContext();
    const viewport = useViewport();

    const windowEntries = useMemo(
        () => Object.values(desktopState.windows),
        [desktopState.windows],
    );

    const handleTaskbarToggle = (id: string) => {
        const window = desktopState.windows[id];
        if (!window) return;
        if (window.minimized) {
            restoreWindow(id);
            return;
        }
        if (desktopState.focusedId === id) {
            minimizeWindow(id);
        } else {
            focusWindow(id);
        }
    };

    return (
        <>
            <div className="absolute inset-0">
                {DESKTOP_ICONS.map((icon, index) => (
                    <DesktopIcon
                        key={icon.id}
                        label={icon.label}
                        icon={icon.icon}
                        position={{ x: 20, y: 20 + index * 110 }}
                        onActivate={() => openWindow(icon.windowId)}
                    />
                ))}
            </div>

            <div
                className="absolute inset-0 pointer-events-none"
                style={{ bottom: TASKBAR_HEIGHT }}
            >
                {windowEntries.map((windowState) => {
                    const preset = WINDOW_PRESETS.find(
                        (entry) =>
                            entry.id === (windowState.appId || windowState.id),
                    );
                    if (!preset) return null;

                    if (windowState.minimized) {
                        return null;
                    }

                    const isMaximized = windowState.maximized;
                    const focused = desktopState.focusedId === windowState.id;

                    const size = isMaximized
                        ? {
                              width: viewport.width,
                              height: viewport.height - TASKBAR_HEIGHT,
                          }
                        : {
                              width: windowState.width,
                              height: windowState.height,
                          };
                    const position = isMaximized
                        ? { x: 0, y: 0 }
                        : { x: windowState.x, y: windowState.y };

                    return (
                        <Rnd
                            key={windowState.id}
                            size={size}
                            position={position}
                            minWidth={MIN_WIDTH}
                            minHeight={MIN_HEIGHT}
                            bounds="parent"
                            dragHandleClassName={DRAG_HANDLE}
                            disableDragging={isMaximized}
                            enableResizing={!isMaximized}
                            style={{
                                zIndex: windowState.zIndex,
                                pointerEvents: 'auto',
                            }}
                            onDragStart={() => focusWindow(windowState.id)}
                            onDragStop={(_e, data) => {
                                updatePosition(windowState.id, {
                                    x: data.x,
                                    y: data.y,
                                });
                            }}
                            onResizeStart={() => focusWindow(windowState.id)}
                            onResizeStop={(_e, _dir, ref, _delta, pos) => {
                                updateSize(windowState.id, {
                                    width: ref.offsetWidth,
                                    height: ref.offsetHeight,
                                });
                                updatePosition(windowState.id, {
                                    x: pos.x,
                                    y: pos.y,
                                });
                            }}
                        >
                            <div
                                className="h-full w-full"
                                onPointerDown={() =>
                                    focusWindow(windowState.id)
                                }
                            >
                                <WindowFrame
                                    id={windowState.id}
                                    title={preset.title}
                                    width={windowState.width}
                                    height={windowState.height}
                                    zIndex={windowState.zIndex}
                                    icon={preset.icon}
                                    focused={focused}
                                    maximized={isMaximized}
                                    dragHandleClassName={DRAG_HANDLE}
                                    onClose={() => closeWindow(windowState.id)}
                                    onMinimize={() =>
                                        minimizeWindow(windowState.id)
                                    }
                                    onMaximize={() =>
                                        toggleMaximize(windowState.id)
                                    }
                                >
                                    <WindowInstanceProvider
                                        windowId={windowState.id}
                                    >
                                        {createElement(
                                            preset.component,
                                            windowState.props ?? {},
                                        )}
                                    </WindowInstanceProvider>
                                </WindowFrame>
                            </div>
                        </Rnd>
                    );
                })}
            </div>

            {desktopState.startMenuOpen && (
                <StartMenu
                    onLaunch={(id) => openWindow(id)}
                    onClose={closeStartMenu}
                />
            )}

            <DesktopTaskbar
                windows={windowEntries.map((window) => ({
                    id: window.id,
                    title: window.title,
                    icon: window.icon,
                    minimized: window.minimized,
                }))}
                focusedId={desktopState.focusedId}
                startMenuOpen={desktopState.startMenuOpen}
                onToggle={handleTaskbarToggle}
                onClose={closeWindow}
                onStartClick={toggleStartMenu}
            />
        </>
    );
}

export default function DesktopExperience() {
    return (
        <section className="fixed inset-0">
            <DesktopWallpaper />
            <DesktopProvider>
                <DesktopWindowLayer />
            </DesktopProvider>
        </section>
    );
}
