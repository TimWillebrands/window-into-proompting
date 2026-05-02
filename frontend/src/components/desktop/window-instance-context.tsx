import { createContext, type ReactNode, useContext } from 'react';

interface WindowInstance {
    windowId: string;
}

const WindowInstanceContext = createContext<WindowInstance | null>(null);

export function WindowInstanceProvider({
    windowId,
    children,
}: {
    windowId: string;
    children: ReactNode;
}) {
    return (
        <WindowInstanceContext.Provider value={{ windowId }}>
            {children}
        </WindowInstanceContext.Provider>
    );
}

export function useWindowInstance(): WindowInstance {
    const ctx = useContext(WindowInstanceContext);
    if (!ctx) {
        throw new Error(
            'useWindowInstance must be called inside a WindowInstanceProvider',
        );
    }
    return ctx;
}
