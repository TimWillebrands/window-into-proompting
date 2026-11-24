import {
    type ComponentPropsWithRef,
    type KeyboardEvent,
    type ReactNode,
    useId,
    useRef,
    useState,
} from 'react';
import {
    type MenuEntry,
    type MenuExit,
    MenuPopup,
    useMenuTree,
} from '../ContextMenu/menu';

export type MenuBarMenu = {
    id?: string;
    label: ReactNode;
    items: MenuEntry[];
    disabled?: boolean;
};

export type MenuBarProps = Omit<ComponentPropsWithRef<'div'>, 'children'> & {
    menus: MenuBarMenu[];
};

export function MenuBar({ menus, className, ...rest }: MenuBarProps) {
    const baseId = useId();
    const buttons = useRef<Array<HTMLButtonElement | null>>([]);
    const [focusIndex, setFocusIndex] = useState(0);
    const [open, setOpen] = useState<{
        index: number;
        focusFirst: boolean;
    } | null>(null);
    const tree = useMenuTree(open !== null, () => setOpen(null));

    const usable = menus.flatMap((menu, index) =>
        menu.disabled ? [] : [index],
    );

    const neighbour = (from: number, step: number) => {
        if (usable.length === 0) return from;
        const at = usable.indexOf(from);
        const next = at < 0 ? 0 : (at + step + usable.length) % usable.length;
        return usable[next];
    };

    const focusTop = (index: number) => {
        setFocusIndex(index);
        buttons.current[index]?.focus();
    };

    const openMenu = (index: number, focusFirst: boolean) => {
        setFocusIndex(index);
        setOpen({ index, focusFirst });
    };

    const onExit = (how: MenuExit) => {
        const index = open?.index ?? focusIndex;
        if (how === 'next' || how === 'prev') {
            const next = neighbour(index, how === 'next' ? 1 : -1);
            openMenu(next, true);
            buttons.current[next]?.focus();
            return;
        }
        setOpen(null);
        if (how === 'close') focusTop(index);
    };

    const onKeyDown = (
        event: KeyboardEvent<HTMLButtonElement>,
        index: number,
    ) => {
        const handled = () => {
            event.preventDefault();
            event.stopPropagation();
        };
        switch (event.key) {
            case 'ArrowRight':
                handled();
                focusTop(neighbour(index, 1));
                return;
            case 'ArrowLeft':
                handled();
                focusTop(neighbour(index, -1));
                return;
            case 'Home':
                handled();
                if (usable.length) focusTop(usable[0]);
                return;
            case 'End':
                handled();
                if (usable.length) focusTop(usable[usable.length - 1]);
                return;
            case 'ArrowDown':
            case 'ArrowUp':
            case 'Enter':
            case ' ':
                handled();
                if (!menus[index]?.disabled) openMenu(index, true);
                return;
            case 'Escape':
                if (open) {
                    handled();
                    setOpen(null);
                }
                return;
        }
    };

    const openButton = open ? buttons.current[open.index] : null;
    const openMenuEntry = open ? menus[open.index] : undefined;

    return (
        <div
            role="menubar"
            aria-label="Menu bar"
            className={className ? `ui-menubar ${className}` : 'ui-menubar'}
            data-ui-menu-tree={tree}
            {...rest}
        >
            {menus.map((menu, index) => (
                <button
                    key={menu.id ?? `menu-${index}`}
                    ref={(element) => {
                        buttons.current[index] = element;
                    }}
                    id={`${baseId}-${index}`}
                    type="button"
                    role="menuitem"
                    className="ui-menubar-item"
                    aria-haspopup="menu"
                    aria-expanded={open?.index === index}
                    aria-disabled={menu.disabled || undefined}
                    tabIndex={index === focusIndex ? 0 : -1}
                    onKeyDown={(event) => onKeyDown(event, index)}
                    onClick={() => {
                        if (menu.disabled) return;
                        if (open?.index === index) setOpen(null);
                        else openMenu(index, false);
                    }}
                    onPointerEnter={() => {
                        if (open && open.index !== index && !menu.disabled)
                            openMenu(index, false);
                    }}
                    onFocus={() => setFocusIndex(index)}
                >
                    {menu.label}
                </button>
            ))}
            {open && openMenuEntry && openButton && (
                <MenuPopup
                    key={open.index}
                    entries={openMenuEntry.items}
                    anchor={{ below: openButton }}
                    tree={tree}
                    owner={openButton}
                    labelledBy={`${baseId}-${open.index}`}
                    focusFirst={open.focusFirst}
                    onExit={onExit}
                />
            )}
        </div>
    );
}
