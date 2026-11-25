import { type MouseEvent, useCallback, useRef, useState } from 'react';
import type { Point } from '../placement';
import { type MenuEntry, type MenuExit, MenuPopup, useMenuTree } from './menu';

export type ContextMenuProps = {
    open: boolean;
    at: Point;
    items: MenuEntry[];
    onClose: () => void;
    label?: string;
    owner?: Element | null;
};

export function ContextMenu({
    open,
    at,
    items,
    onClose,
    label,
    owner = null,
}: ContextMenuProps) {
    const returnTo = useRef<HTMLElement | null>(null);
    const [wasOpen, setWasOpen] = useState(false);
    if (open !== wasOpen) {
        setWasOpen(open);
        if (open && typeof document !== 'undefined')
            returnTo.current =
                document.activeElement instanceof HTMLElement
                    ? document.activeElement
                    : null;
    }
    const tree = useMenuTree(open, onClose);
    if (!open) return null;
    const exit = (how: MenuExit) => {
        if (how === 'next' || how === 'prev') return;
        onClose();
        if (returnTo.current?.isConnected) returnTo.current.focus();
    };
    return (
        <MenuPopup
            entries={items}
            anchor={{ at }}
            tree={tree}
            owner={owner ?? returnTo.current}
            label={label}
            focusFirst={false}
            onExit={exit}
        />
    );
}

export function useContextMenu() {
    const [state, setState] = useState<{
        open: boolean;
        at: Point;
        owner: Element | null;
    }>({ open: false, at: { x: 0, y: 0 }, owner: null });

    const onContextMenu = useCallback((event: MouseEvent<Element>) => {
        event.preventDefault();
        const target = event.currentTarget;
        const keyboard = event.clientX === 0 && event.clientY === 0;
        const rect = target.getBoundingClientRect();
        setState({
            open: true,
            at: keyboard
                ? { x: rect.left + 8, y: rect.top + rect.height / 2 }
                : { x: event.clientX, y: event.clientY },
            owner: target,
        });
    }, []);

    const openAt = useCallback(
        (at: Point, owner: Element | null = null) =>
            setState({ open: true, at, owner }),
        [],
    );

    const onClose = useCallback(
        () => setState((previous) => ({ ...previous, open: false })),
        [],
    );

    return {
        onContextMenu,
        openAt,
        menuProps: {
            open: state.open,
            at: state.at,
            owner: state.owner,
            onClose,
        },
    };
}
