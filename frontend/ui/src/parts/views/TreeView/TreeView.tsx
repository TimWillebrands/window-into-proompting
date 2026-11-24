import {
    type ComponentPropsWithRef,
    type KeyboardEvent,
    type MouseEvent,
    type ReactNode,
    useEffect,
    useRef,
    useState,
} from 'react';

export type TreeItem = {
    id: string;
    label: ReactNode;
    icon?: ReactNode;
    openIcon?: ReactNode;
    disabled?: boolean;
    children?: ReadonlyArray<TreeItem>;
};

export type TreeViewProps = Omit<
    ComponentPropsWithRef<'ul'>,
    'children' | 'onSelect'
> & {
    items: ReadonlyArray<TreeItem>;
    expanded?: ReadonlyArray<string>;
    defaultExpanded?: ReadonlyArray<string>;
    onExpandedChange?: (expanded: string[]) => void;
    selected?: string;
    defaultSelected?: string;
    onSelect?: (id: string) => void;
    onActivate?: (id: string) => void;
    label?: string;
    lines?: boolean;
};

type Visible = {
    item: TreeItem;
    parent?: string;
    level: number;
};

const flatten = (
    items: ReadonlyArray<TreeItem>,
    open: Set<string>,
    level = 1,
    parent?: string,
): Visible[] =>
    items.flatMap((item) => [
        { item, parent, level },
        ...(item.children && open.has(item.id)
            ? flatten(item.children, open, level + 1, item.id)
            : []),
    ]);

export function TreeView({
    items,
    expanded,
    defaultExpanded,
    onExpandedChange,
    selected,
    defaultSelected,
    onSelect,
    onActivate,
    label,
    lines = true,
    className,
    ref,
    ...rest
}: TreeViewProps) {
    const root = useRef<HTMLUListElement | null>(null);
    const [ownOpen, setOwnOpen] = useState<ReadonlyArray<string>>(
        defaultExpanded ?? [],
    );
    const open = new Set(expanded ?? ownOpen);
    const [ownSelected, setOwnSelected] = useState(defaultSelected);
    const current = selected ?? ownSelected;
    const visible = flatten(items, open);
    const [focused, setFocused] = useState<string | undefined>();
    const [moved, setMoved] = useState(false);
    const tabStop =
        visible.find((entry) => entry.item.id === focused)?.item.id ??
        visible.find((entry) => entry.item.id === current)?.item.id ??
        visible[0]?.item.id;

    useEffect(() => {
        if (!moved || !focused) return;
        setMoved(false);
        root.current
            ?.querySelector<HTMLElement>(
                `[data-tree-item="${CSS.escape(focused)}"]`,
            )
            ?.focus();
    }, [moved, focused]);

    const setOpen = (id: string, value: boolean) => {
        const next = new Set(open);
        if (value) next.add(id);
        else next.delete(id);
        const list = Array.from(next);
        setOwnOpen(list);
        onExpandedChange?.(list);
    };

    const select = (id: string) => {
        setOwnSelected(id);
        onSelect?.(id);
    };

    const moveTo = (id: string | undefined) => {
        if (!id) return;
        setFocused(id);
        setMoved(true);
        select(id);
    };

    const handleKeyDown = (event: KeyboardEvent<HTMLUListElement>) => {
        const index = visible.findIndex((entry) => entry.item.id === tabStop);
        const entry = visible[index];
        if (!entry) return;
        const { item } = entry;
        const hasChildren = Boolean(item.children?.length);
        const isOpen = open.has(item.id);
        const step = (delta: number) => {
            for (
                let i = index + delta;
                i >= 0 && i < visible.length;
                i += delta
            )
                if (!visible[i].item.disabled) return visible[i].item.id;
            return undefined;
        };
        let handled = true;
        switch (event.key) {
            case 'ArrowDown':
                moveTo(step(1));
                break;
            case 'ArrowUp':
                moveTo(step(-1));
                break;
            case 'ArrowRight':
                if (hasChildren && !isOpen) setOpen(item.id, true);
                else if (hasChildren) moveTo(visible[index + 1]?.item.id);
                break;
            case 'ArrowLeft':
                if (hasChildren && isOpen) setOpen(item.id, false);
                else moveTo(entry.parent);
                break;
            case 'Home':
                moveTo(visible.find((v) => !v.item.disabled)?.item.id);
                break;
            case 'End':
                moveTo(
                    [...visible].reverse().find((v) => !v.item.disabled)?.item
                        .id,
                );
                break;
            case 'Enter':
                if (!item.disabled) {
                    select(item.id);
                    onActivate?.(item.id);
                }
                break;
            case '*': {
                const siblings = visible.filter(
                    (v) => v.parent === entry.parent && v.item.children?.length,
                );
                const next = new Set(open);
                for (const sibling of siblings) next.add(sibling.item.id);
                const list = Array.from(next);
                setOwnOpen(list);
                onExpandedChange?.(list);
                break;
            }
            default:
                handled = false;
        }
        if (handled) event.preventDefault();
    };

    const entryAt = (target: EventTarget) => {
        const node = (target as Element).closest<HTMLElement>(
            '[data-tree-item]',
        );
        const id = node?.dataset.treeItem;
        const entry = visible.find((v) => v.item.id === id);
        return entry && !entry.item.disabled ? entry : undefined;
    };

    const handleClick = (event: MouseEvent<HTMLUListElement>) => {
        const entry = entryAt(event.target);
        if (!entry) return;
        const { item } = entry;
        const onExpander = (event.target as Element).closest(
            '.ui-treeview-expander',
        );
        if (onExpander && item.children?.length)
            setOpen(item.id, !open.has(item.id));
        setFocused(item.id);
        setMoved(true);
        select(item.id);
    };

    const handleDoubleClick = (event: MouseEvent<HTMLUListElement>) => {
        const entry = entryAt(event.target);
        if (!entry) return;
        if ((event.target as Element).closest('.ui-treeview-expander')) return;
        const { item } = entry;
        if (item.children?.length) setOpen(item.id, !open.has(item.id));
        onActivate?.(item.id);
    };

    const renderItems = (list: ReadonlyArray<TreeItem>, level: number) =>
        list.map((item, index) => {
            const hasChildren = Boolean(item.children?.length);
            const isOpen = hasChildren && open.has(item.id);
            const icon = isOpen ? (item.openIcon ?? item.icon) : item.icon;
            return (
                <li
                    key={item.id}
                    role="treeitem"
                    tabIndex={item.id === tabStop ? 0 : -1}
                    aria-level={level}
                    aria-setsize={list.length}
                    aria-posinset={index + 1}
                    aria-expanded={hasChildren ? isOpen : undefined}
                    aria-selected={item.id === current}
                    aria-disabled={item.disabled || undefined}
                    className="ui-treeview-item"
                    data-tree-item={item.id}
                    data-last={index === list.length - 1 ? '' : undefined}
                    onFocus={(event) => {
                        if (event.target === event.currentTarget)
                            setFocused(item.id);
                    }}
                >
                    <div className="ui-treeview-row">
                        <span
                            className="ui-treeview-expander"
                            data-state={
                                hasChildren
                                    ? isOpen
                                        ? 'open'
                                        : 'closed'
                                    : undefined
                            }
                            aria-hidden="true"
                        />
                        {icon && (
                            <span
                                className="ui-treeview-icon"
                                aria-hidden="true"
                            >
                                {icon}
                            </span>
                        )}
                        <span className="ui-treeview-label">{item.label}</span>
                    </div>
                    {isOpen && item.children && (
                        // biome-ignore lint/a11y/useSemanticElements: an ARIA tree's nested group is a list
                        <ul role="group" className="ui-treeview-group">
                            {renderItems(item.children, level + 1)}
                        </ul>
                    )}
                </li>
            );
        });

    return (
        <ul
            ref={(node) => {
                root.current = node;
                if (typeof ref === 'function') return ref(node);
                if (ref) ref.current = node;
            }}
            // biome-ignore lint/a11y/noNoninteractiveElementToInteractiveRole: the ARIA tree pattern is built on a list
            role="tree"
            aria-label={label}
            className={className ? `ui-treeview ${className}` : 'ui-treeview'}
            data-lines={lines ? '' : undefined}
            onKeyDown={handleKeyDown}
            onClick={handleClick}
            onDoubleClick={handleDoubleClick}
            {...rest}
        >
            {renderItems(items, 1)}
        </ul>
    );
}
