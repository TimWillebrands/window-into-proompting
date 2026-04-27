import {
    createContext,
    type ReactNode,
    useContext,
    useEffect,
    useState,
} from 'react';

interface TitleBarSlot {
    content: ReactNode;
    setContent: (node: ReactNode) => void;
}

const WindowTitleBarContext = createContext<TitleBarSlot | null>(null);

export function WindowTitleBarProvider({ children }: { children: ReactNode }) {
    const [content, setContent] = useState<ReactNode>(null);
    return (
        <WindowTitleBarContext.Provider value={{ content, setContent }}>
            {children}
        </WindowTitleBarContext.Provider>
    );
}

export function useWindowTitleBarSlot() {
    return useContext(WindowTitleBarContext);
}

/** Apps call this hook to push live JSX into their hosting window's title bar. */
export function useSetWindowTitleBar(node: ReactNode) {
    const slot = useContext(WindowTitleBarContext);
    useEffect(() => {
        if (!slot) return;
        slot.setContent(node);
        return () => slot.setContent(null);
    }, [slot, node]);
}
