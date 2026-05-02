import { type ReactNode, Suspense } from 'react';
import {
    useWindowTitleBarSlot,
    WindowTitleBarProvider,
} from './window-titlebar-context';

interface WindowFrameProps {
    id: string;
    title: string;
    width: number;
    height: number;
    zIndex: number;
    icon?: string;
    focused?: boolean;
    maximized?: boolean;
    onMinimize: () => void;
    onMaximize?: () => void;
    onClose: () => void;
    /** Class added to the title bar element so an external drag handler (e.g. react-rnd) can target it. */
    dragHandleClassName?: string;
    children: ReactNode;
}

function TitleBarSlotContent() {
    const slot = useWindowTitleBarSlot();
    const node = slot?.content;
    if (!node) return null;
    return (
        <div className="pointer-events-none absolute inset-y-0 left-1/2 -translate-x-1/2 flex items-center justify-center max-w-[60%]">
            {/* biome-ignore lint/a11y/noStaticElementInteractions: event-swallow wrapper around the interactive slot child. */}
            <div
                className="pointer-events-auto"
                onPointerDown={(e) => e.stopPropagation()}
                onMouseDown={(e) => e.stopPropagation()}
                onTouchStart={(e) => e.stopPropagation()}
            >
                {node}
            </div>
        </div>
    );
}

function isImagePath(s: string) {
    return /\.(png|jpe?g|webp|svg|ico|gif)$/i.test(s) || s.startsWith('/');
}

function TitleBarIcon({ icon }: { icon: string }) {
    if (isImagePath(icon)) {
        return (
            <img src={icon} alt="" className="xp-titlebar-icon" aria-hidden />
        );
    }
    return (
        <span aria-hidden className="text-[14px] leading-none mx-1">
            {icon}
        </span>
    );
}

export default function WindowFrame({
    id,
    title,
    icon = '📄',
    focused = true,
    maximized = false,
    onMinimize,
    onMaximize,
    onClose,
    dragHandleClassName = 'window-drag-handle',
    children,
}: WindowFrameProps) {
    const shellClasses = [
        'xp-window-shell',
        focused ? 'xp-active' : '',
        maximized ? 'xp-maximized' : '',
        'flex flex-col h-full w-full overflow-hidden',
    ]
        .filter(Boolean)
        .join(' ');

    return (
        <WindowTitleBarProvider>
            <div
                data-window-id={id}
                data-focused={focused ? 'true' : 'false'}
                className={shellClasses}
                style={{
                    boxShadow: maximized
                        ? undefined
                        : '0 18px 42px -10px rgba(0,0,0,0.45), 0 4px 14px -4px rgba(0,0,0,0.25)',
                }}
            >
                {/* biome-ignore lint/a11y/noStaticElementInteractions: drag handle for window chrome — react-rnd needs a class on a static element */}
                <div
                    className={`${dragHandleClassName} xp-titlebar relative`}
                    onDoubleClick={() => onMaximize?.()}
                >
                    <TitleBarIcon icon={icon} />
                    <span className="xp-titlebar-title">{title}</span>
                    <TitleBarSlotContent />
                    <span className="xp-titlebar-controls">
                        <button
                            type="button"
                            data-button="minimize"
                            className="xp-titlebar-btn"
                            aria-label="Minimize"
                            title="Minimize"
                            onPointerDown={(e) => e.stopPropagation()}
                            onMouseDown={(e) => e.stopPropagation()}
                            onClick={(e) => {
                                e.stopPropagation();
                                onMinimize();
                            }}
                        >
                            Minimize
                        </button>
                        <button
                            type="button"
                            data-button="maximize"
                            data-maximized={maximized ? 'true' : 'false'}
                            className="xp-titlebar-btn"
                            aria-label={maximized ? 'Restore' : 'Maximize'}
                            title={maximized ? 'Restore' : 'Maximize'}
                            onPointerDown={(e) => e.stopPropagation()}
                            onMouseDown={(e) => e.stopPropagation()}
                            onClick={(e) => {
                                e.stopPropagation();
                                onMaximize?.();
                            }}
                        >
                            {maximized ? 'Restore' : 'Maximize'}
                        </button>
                        <button
                            type="button"
                            data-button="close"
                            className="xp-titlebar-btn"
                            aria-label="Close"
                            title="Close"
                            onPointerDown={(e) => e.stopPropagation()}
                            onMouseDown={(e) => e.stopPropagation()}
                            onClick={(e) => {
                                e.stopPropagation();
                                onClose();
                            }}
                        >
                            Close
                        </button>
                    </span>
                </div>
                <section className="xp-window-content dark:bg-slate-900">
                    <Suspense fallback={<progress>Loading {title}</progress>}>
                        {children}
                    </Suspense>
                </section>
            </div>
        </WindowTitleBarProvider>
    );
}
