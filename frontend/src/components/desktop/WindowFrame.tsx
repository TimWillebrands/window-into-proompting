import { minimiseWithTransition, ProgressBar, Window } from '@partytown/ui';
import { type ReactNode, Suspense } from 'react';
import AppIcon from './AppIcon';
import WindowErrorBoundary from './WindowErrorBoundary';
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
    dragHandleClassName?: string;
    children: ReactNode;
}

function FrameWindow({
    id,
    title,
    icon,
    focused = true,
    maximized = false,
    onMinimize,
    onMaximize,
    onClose,
    dragHandleClassName = 'window-drag-handle',
    children,
}: WindowFrameProps) {
    const slot = useWindowTitleBarSlot();
    return (
        <Window
            windowId={id}
            data-window-id={id}
            title={title}
            icon={<AppIcon icon={icon} />}
            active={focused}
            maximised={maximized}
            titleBarClassName={dragHandleClassName}
            titleBarAccessory={slot?.content ?? undefined}
            onMinimise={() => minimiseWithTransition(id, onMinimize)}
            onMaximise={onMaximize}
            onClose={onClose}
            className="h-full w-full"
        >
            <WindowErrorBoundary windowTitle={title} onClose={onClose}>
                <Suspense
                    fallback={
                        <div className="flex h-full items-center justify-center p-4">
                            <ProgressBar
                                marquee
                                label={`Loading ${title}`}
                                className="w-[160px]"
                            />
                        </div>
                    }
                >
                    {children}
                </Suspense>
            </WindowErrorBoundary>
        </Window>
    );
}

export default function WindowFrame(props: WindowFrameProps) {
    return (
        <WindowTitleBarProvider>
            <FrameWindow {...props} />
        </WindowTitleBarProvider>
    );
}
