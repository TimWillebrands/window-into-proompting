import { type ReactNode, Suspense } from 'react';

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

export default function WindowFrame({
    id,
    title,
    width,
    height,
    zIndex,
    icon = '📄',
    onMinimize,
    onRestore,
    onClose,
    children,
}: WindowFrameProps) {
    return (
        <div
            className="window window-frame flex flex-col overflow-hidden"
            data-window-id={id}
            style={{
                width: '100%',
                height: '100%',
            }}
        >
            <div className="title-bar box-content cursor-grab select-none flex justify-between items-center px-1 drag-handle">
                <div className="title-bar-text flex items-center">
                    <span className="mr-1">{icon}</span>
                    {title}
                </div>
                <div className="title-bar-controls flex">
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
            <section className="window-body flex-1 overflow-hidden bg-[#ECE9D8]">
                <Suspense fallback={<progress>Loading {title}</progress>}>
                    {children}
                </Suspense>
            </section>
        </div>
    );
}
