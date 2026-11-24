import { ProgressBar } from '@partytown/ui';
import {
    type ComponentType,
    createElement,
    type ReactNode,
    Suspense,
} from 'react';
import { useDesktopStore } from '../../lib/desktop-context';
import WindowErrorBoundary from './WindowErrorBoundary';
import { WindowInstanceProvider } from './window-instance-context';

export function ProgramBody({
    title,
    onClose,
    children,
}: {
    title: string;
    onClose?: () => void;
    children: ReactNode;
}) {
    return (
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
    );
}

export default function ProgramHost({
    id,
    title,
    component,
    props,
}: {
    id: string;
    title: string;
    // biome-ignore lint/suspicious/noExplicitAny: each program takes its own props from the window's opaque record
    component: ComponentType<any>;
    props: Record<string, unknown>;
}) {
    const closeWindow = useDesktopStore((s) => s.closeWindow);
    return (
        <WindowInstanceProvider windowId={id}>
            <ProgramBody title={title} onClose={() => closeWindow(id)}>
                {createElement(component, props)}
            </ProgramBody>
        </WindowInstanceProvider>
    );
}
