import {
    type CSSProperties,
    type KeyboardEvent,
    type ReactNode,
    useEffect,
    useLayoutEffect,
    useRef,
    useState,
} from 'react';
import type { Tone } from '../../../tone';
import { Glyph } from '../glyphs';
import { type Point, placeAtPoint, placeBeside, type Rect } from '../placement';
import { ChromePortal } from '../portal';

export type MenuItem = {
    type?: 'item';
    id?: string;
    label: ReactNode;
    icon?: ReactNode;
    shortcut?: ReactNode;
    disabled?: boolean;
    checked?: boolean;
    tone?: Tone;
    onSelect?: () => void;
    items?: MenuEntry[];
};

export type MenuSeparator = { type: 'separator'; id?: string };

export type MenuEntry = MenuItem | MenuSeparator;

export type MenuExit = 'close' | 'select' | 'back' | 'next' | 'prev';

export type MenuAnchor =
    | { at: Point }
    | { below: Element }
    | { beside: Element };

const isItem = (entry: MenuEntry): entry is MenuItem =>
    entry.type !== 'separator';

const enabledIndexes = (entries: MenuEntry[]) =>
    entries.flatMap((entry, index) =>
        isItem(entry) && !entry.disabled ? [index] : [],
    );

const rectOf = (element: Element): Rect => {
    const r = element.getBoundingClientRect();
    return {
        left: r.left,
        top: r.top,
        right: r.right,
        bottom: r.bottom,
        width: r.width,
        height: r.height,
    };
};

function place(anchor: MenuAnchor, size: { width: number; height: number }) {
    if ('beside' in anchor) {
        const rect = rectOf(anchor.beside);
        const point = placeBeside(rect, size, 2);
        return {
            ...point,
            fromX: point.x < rect.left ? 6 : -6,
            fromY: 0,
        };
    }
    const origin =
        'below' in anchor
            ? {
                  x: rectOf(anchor.below).left,
                  y: rectOf(anchor.below).bottom,
              }
            : anchor.at;
    const point = placeAtPoint(origin, size);
    return { ...point, fromX: 0, fromY: point.y < origin.y ? 6 : -6 };
}

export type MenuPopupProps = {
    entries: MenuEntry[];
    anchor: MenuAnchor;
    tree: string;
    owner: Element | null;
    depth?: number;
    label?: string;
    labelledBy?: string;
    focusFirst?: boolean;
    onExit: (how: MenuExit) => void;
};

export function MenuPopup({
    entries,
    anchor,
    tree,
    owner,
    depth = 0,
    label,
    labelledBy,
    focusFirst = false,
    onExit,
}: MenuPopupProps) {
    const [menu, setMenu] = useState<HTMLDivElement | null>(null);
    const itemRefs = useRef<Array<HTMLDivElement | null>>([]);
    const [placed, setPlaced] = useState<{
        x: number;
        y: number;
        fromX: number;
        fromY: number;
    } | null>(null);
    const [active, setActive] = useState(-1);
    const [open, setOpen] = useState<{
        index: number;
        focusFirst: boolean;
    } | null>(null);
    const enabled = enabledIndexes(entries);

    const focused = useRef(false);
    const firstEnabled = enabled[0] ?? -1;

    useLayoutEffect(() => {
        if (!menu) return;
        const next = place(anchor, {
            width: menu.offsetWidth,
            height: menu.offsetHeight,
        });
        setPlaced((previous) =>
            previous &&
            previous.x === next.x &&
            previous.y === next.y &&
            previous.fromX === next.fromX &&
            previous.fromY === next.fromY
                ? previous
                : next,
        );
    });

    useEffect(() => {
        if (!placed || focused.current) return;
        focused.current = true;
        if (focusFirst && firstEnabled >= 0) setActive(firstEnabled);
        else menu?.focus({ preventScroll: true });
    }, [placed, focusFirst, firstEnabled, menu]);

    useEffect(() => {
        if (active >= 0)
            itemRefs.current[active]?.focus({ preventScroll: true });
    }, [active]);

    const move = (step: number) => {
        if (enabled.length === 0) return;
        const at = enabled.indexOf(active);
        const next =
            at < 0
                ? step > 0
                    ? 0
                    : enabled.length - 1
                : (at + step + enabled.length) % enabled.length;
        setActive(enabled[next]);
    };

    const activate = (index: number, viaKeyboard: boolean) => {
        const entry = entries[index];
        if (!entry || !isItem(entry) || entry.disabled) return;
        if (entry.items) {
            setActive(index);
            setOpen({ index, focusFirst: viaKeyboard });
            return;
        }
        entry.onSelect?.();
        onExit('select');
    };

    const typeahead = (key: string) => {
        const letter = key.toLocaleLowerCase();
        const start = enabled.indexOf(active);
        const order = [
            ...enabled.slice(start + 1),
            ...enabled.slice(0, start + 1),
        ];
        const hit = order.find((index) =>
            itemRefs.current[index]?.textContent
                ?.trim()
                .toLocaleLowerCase()
                .startsWith(letter),
        );
        if (hit !== undefined) setActive(hit);
    };

    const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        const handled = () => {
            event.preventDefault();
            event.stopPropagation();
        };
        switch (event.key) {
            case 'ArrowDown':
                handled();
                move(1);
                return;
            case 'ArrowUp':
                handled();
                move(-1);
                return;
            case 'Home':
                handled();
                if (enabled.length) setActive(enabled[0]);
                return;
            case 'End':
                handled();
                if (enabled.length) setActive(enabled[enabled.length - 1]);
                return;
            case 'ArrowRight': {
                handled();
                const entry = entries[active];
                if (entry && isItem(entry) && entry.items && !entry.disabled)
                    setOpen({ index: active, focusFirst: true });
                else onExit('next');
                return;
            }
            case 'ArrowLeft':
                handled();
                onExit(depth > 0 ? 'back' : 'prev');
                return;
            case 'Escape':
                handled();
                onExit(depth > 0 ? 'back' : 'close');
                return;
            case 'Enter':
            case ' ':
                handled();
                if (active >= 0) activate(active, true);
                return;
            case 'Tab':
                handled();
                onExit('close');
                return;
            default:
                if (
                    event.key.length === 1 &&
                    !event.ctrlKey &&
                    !event.metaKey &&
                    !event.altKey
                ) {
                    handled();
                    typeahead(event.key);
                }
        }
    };

    const onChildExit = (how: MenuExit) => {
        if (how === 'back') {
            const index = open?.index ?? -1;
            setOpen(null);
            if (index >= 0) itemRefs.current[index]?.focus();
            return;
        }
        setOpen(null);
        onExit(how);
    };

    const openEntry = open ? entries[open.index] : undefined;
    const openItem = openEntry && isItem(openEntry) ? openEntry : undefined;
    const openElement = open ? itemRefs.current[open.index] : null;

    const style = {
        left: placed?.x ?? 0,
        top: placed?.y ?? 0,
        visibility: placed ? undefined : 'hidden',
        '--ui-float-x': `${placed?.fromX ?? 0}px`,
        '--ui-float-y': `${placed?.fromY ?? 0}px`,
    } as CSSProperties;

    return (
        <ChromePortal owner={owner}>
            <div
                ref={setMenu}
                role="menu"
                tabIndex={-1}
                aria-label={label}
                aria-labelledby={labelledBy}
                className="ui-menu"
                data-ui-menu-tree={tree}
                data-placed={placed ? '' : undefined}
                style={style}
                onKeyDown={onKeyDown}
                onContextMenu={(event) => event.preventDefault()}
            >
                {entries.map((entry, index) => {
                    const key = entry.id ?? `entry-${index}`;
                    if (!isItem(entry))
                        return <hr key={key} className="ui-menu-separator" />;
                    const hasItems = !!entry.items;
                    const role =
                        entry.checked === undefined
                            ? 'menuitem'
                            : 'menuitemcheckbox';
                    return (
                        // biome-ignore lint/a11y/noStaticElementInteractions: the role is menuitem or menuitemcheckbox, chosen per entry
                        // biome-ignore lint/a11y/useAriaPropsSupportedByRole: aria-checked is only set on menuitemcheckbox
                        <div
                            key={key}
                            ref={(element) => {
                                itemRefs.current[index] = element;
                            }}
                            role={role}
                            tabIndex={-1}
                            aria-disabled={entry.disabled || undefined}
                            aria-checked={
                                entry.checked === undefined
                                    ? undefined
                                    : entry.checked
                            }
                            aria-haspopup={hasItems ? 'menu' : undefined}
                            aria-expanded={
                                hasItems ? open?.index === index : undefined
                            }
                            className="ui-menu-item"
                            data-tone={entry.tone}
                            data-active={active === index ? '' : undefined}
                            onPointerEnter={() => {
                                if (entry.disabled) {
                                    setActive(-1);
                                    setOpen(null);
                                    return;
                                }
                                setActive(index);
                                setOpen(
                                    hasItems
                                        ? { index, focusFirst: false }
                                        : null,
                                );
                            }}
                            onClick={() => activate(index, false)}
                            onKeyDown={() => undefined}
                        >
                            <span className="ui-menu-mark" aria-hidden="true">
                                {entry.checked ? (
                                    <Glyph name="check" />
                                ) : (
                                    entry.icon
                                )}
                            </span>
                            <span className="ui-menu-label">{entry.label}</span>
                            <span className="ui-menu-shortcut">
                                {entry.shortcut}
                            </span>
                            <span className="ui-menu-arrow" aria-hidden="true">
                                {hasItems && <Glyph name="submenu" />}
                            </span>
                        </div>
                    );
                })}
            </div>
            {openItem?.items && openElement && (
                <MenuPopup
                    key={open?.index}
                    entries={openItem.items}
                    anchor={{ beside: openElement }}
                    tree={tree}
                    owner={owner}
                    depth={depth + 1}
                    focusFirst={open?.focusFirst}
                    onExit={onChildExit}
                />
            )}
        </ChromePortal>
    );
}

export function useMenuTree(open: boolean, onOutside: () => void) {
    const [tree] = useState(
        () => `ui-menu-${Math.random().toString(36).slice(2, 10)}`,
    );
    const outside = useRef(onOutside);
    outside.current = onOutside;
    useEffect(() => {
        if (!open) return;
        const onPointer = (event: PointerEvent) => {
            const target = event.target;
            if (
                target instanceof Element &&
                target.closest(`[data-ui-menu-tree="${tree}"]`)
            )
                return;
            outside.current();
        };
        const onBlur = () => outside.current();
        document.addEventListener('pointerdown', onPointer, true);
        window.addEventListener('blur', onBlur);
        return () => {
            document.removeEventListener('pointerdown', onPointer, true);
            window.removeEventListener('blur', onBlur);
        };
    }, [open, tree]);
    return tree;
}
