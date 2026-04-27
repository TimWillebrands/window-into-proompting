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
    onMinimize: () => void;
    onRestore: () => void;
    onClose: () => void;
    children: ReactNode;
}

function TitleBarSlotContent() {
    const slot = useWindowTitleBarSlot();
    const node = slot?.content;
    if (!node) return null;
    // Stop drag handle from grabbing pointer events on slot content (e.g. inputs).
    return (
        // biome-ignore lint/a11y/noStaticElementInteractions: pure event-swallow wrapper, not interactive itself.
        <div
            className="flex-1 min-w-0 flex items-center justify-center mx-2"
            onPointerDown={(e) => e.stopPropagation()}
            onMouseDown={(e) => e.stopPropagation()}
            onTouchStart={(e) => e.stopPropagation()}
        >
            {node}
        </div>
    );
}

export default function WindowFrame({
    id,
    title,
    icon = '📄',
    onMinimize,
    onRestore,
    onClose,
    children,
}: WindowFrameProps) {
    return (
        <WindowTitleBarProvider>
            <div
                className="window window-frame flex flex-col overflow-hidden"
                data-window-id={id}
                style={{
                    width: '100%',
                    height: '100%',
                }}
            >
                <div className="title-bar box-content cursor-grab select-none flex justify-between items-center px-1 drag-handle">
                    <div className="title-bar-text flex items-center shrink-0">
                        <span className="mr-1">{icon}</span>
                        {title}
                    </div>
                    <TitleBarSlotContent />
                    <div className="title-bar-controls flex shrink-0">
                        <button
                            type="button"
                            aria-label="Minimize"
                            onClick={onMinimize}
                        />
                        <button
                            type="button"
                            aria-label="Restore"
                            onClick={onRestore}
                        />
                        <button
                            type="button"
                            aria-label="Close"
                            onClick={onClose}
                        />
                    </div>
                </div>
                <section className="window-body flex-1 overflow-hidden bg-[#ECE9D8] dark:bg-slate-900">
                    <Suspense fallback={<progress>Loading {title}</progress>}>
                        {children}
                    </Suspense>
                </section>
            </div>
        </WindowTitleBarProvider>
    );
}
