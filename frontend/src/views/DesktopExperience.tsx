import { useMemo, createElement } from 'react';
import GridLayout from 'react-grid-layout';
import 'react-grid-layout/css/styles.css';
import DesktopIcon from '../components/desktop/DesktopIcon';
import DesktopTaskbar from '../components/desktop/DesktopTaskbar';
import DesktopWallpaper from '../components/desktop/DesktopWallpaper';
import WindowFrame from '../components/desktop/WindowFrame';
import { DesktopProvider, useDesktopContext } from '../lib/desktop-context';
import { DESKTOP_ICONS, WINDOW_PRESETS } from '../lib/window-presets';

/** Pixel size of one grid cell — kept large so windows snap to coarse grid */
const GRID_CELL = 1;
const GRID_COLS = 2000;

function DesktopWindowLayer() {
    const {
        desktopState,
        openWindow,
        focusWindow,
        closeWindow,
        updateSize,
        updatePosition,
    } = useDesktopContext();

    const windowEntries = useMemo(
        () => Object.values(desktopState.windows),
        [desktopState.windows],
    );

    const layout = useMemo(
        () =>
            windowEntries.map((w) => ({
                i: w.id,
                x: Math.round(w.x / GRID_CELL),
                y: Math.round(w.y / GRID_CELL),
                w: Math.round(w.width / GRID_CELL),
                h: Math.round(w.height / GRID_CELL),
                minW: Math.round(500 / GRID_CELL),
                minH: Math.round(350 / GRID_CELL),
            })),
        [windowEntries],
    );

    const handleLayoutChange = (newLayout: GridLayout.Layout[]) => {
        for (const item of newLayout) {
            const existing = desktopState.windows[item.i];
            if (!existing) continue;

            const newX = item.x * GRID_CELL;
            const newY = item.y * GRID_CELL;
            const newW = item.w * GRID_CELL;
            const newH = item.h * GRID_CELL;

            if (existing.x !== newX || existing.y !== newY) {
                updatePosition(item.i, { x: newX, y: newY });
            }
            if (existing.width !== newW || existing.height !== newH) {
                updateSize(item.i, { width: newW, height: newH });
            }
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

            <GridLayout
                className="absolute inset-0 pointer-events-none"
                layout={layout}
                cols={GRID_COLS}
                rowHeight={GRID_CELL}
                width={GRID_COLS * GRID_CELL}
                compactType={null}
                allowOverlap={true}
                isResizable={true}
                isDraggable={true}
                draggableHandle=".drag-handle"
                onLayoutChange={handleLayoutChange}
                margin={[0, 0]}
                containerPadding={[0, 0]}
                useCSSTransforms={true}
            >
                {windowEntries.map((windowState) => {
                    const preset = WINDOW_PRESETS.find(
                        (entry) =>
                            entry.id === (windowState.appId || windowState.id),
                    );
                    if (!preset) return null;

                    return (
                        <div
                            key={windowState.id}
                            className="pointer-events-auto"
                            style={{ zIndex: windowState.zIndex }}
                            onPointerDown={() => focusWindow(windowState.id)}
                        >
                            <WindowFrame
                                id={windowState.id}
                                title={preset.title}
                                width={windowState.width}
                                height={windowState.height}
                                zIndex={windowState.zIndex}
                                icon={preset.icon}
                                onClose={() => closeWindow(windowState.id)}
                                onMinimize={() => closeWindow(windowState.id)}
                                onRestore={() => closeWindow(windowState.id)}
                            >
                                {createElement(
                                    preset.component,
                                    windowState.props ?? {},
                                )}
                            </WindowFrame>
                        </div>
                    );
                })}
            </GridLayout>

            <DesktopTaskbar
                windows={Object.values(desktopState.windows).map((window) => ({
                    id: window.id,
                    title: window.title,
                    icon: window.icon,
                }))}
                focusedId={desktopState.focusedId}
                onToggle={focusWindow}
                onClose={closeWindow}
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
