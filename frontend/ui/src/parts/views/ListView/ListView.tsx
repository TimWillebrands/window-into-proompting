import {
    type ComponentPropsWithRef,
    type CSSProperties,
    type KeyboardEvent,
    type MouseEvent,
    type ReactNode,
    useEffect,
    useRef,
    useState,
} from 'react';
import type { Tone } from '../../../tone';
import { type Direction, neighbour } from './neighbour';

export type ListViewMode = 'details' | 'tiles' | 'icons' | 'list';

export type ListViewItem = {
    id: string;
    label: ReactNode;
    icon?: ReactNode;
    largeIcon?: ReactNode;
    details?: ReadonlyArray<ReactNode>;
    cells?: Readonly<Record<string, ReactNode>>;
    disabled?: boolean;
    tone?: Tone;
};

export type ListViewColumn = {
    key: string;
    header: ReactNode;
    width?: string;
    align?: 'start' | 'center' | 'end';
    sortable?: boolean;
    wrap?: boolean;
};

export type ListViewSort = {
    key: string;
    direction: 'ascending' | 'descending';
};

export type ListViewProps = Omit<
    ComponentPropsWithRef<'div'>,
    'children' | 'onSelect'
> & {
    view?: ListViewMode;
    items: ReadonlyArray<ListViewItem>;
    columns?: ReadonlyArray<ListViewColumn>;
    selectionMode?: 'none' | 'single' | 'multiple';
    selected?: ReadonlyArray<string>;
    defaultSelected?: ReadonlyArray<string>;
    onSelectionChange?: (selected: string[]) => void;
    onActivate?: (id: string) => void;
    activateOn?: 'double' | 'single';
    tileSize?: 'regular' | 'compact';
    sort?: ListViewSort;
    onSortChange?: (sort: ListViewSort) => void;
    label?: string;
    empty?: ReactNode;
};

const arrows: Record<string, Direction> = {
    ArrowUp: 'up',
    ArrowDown: 'down',
    ArrowLeft: 'left',
    ArrowRight: 'right',
};

const columnTemplate = (columns: ReadonlyArray<ListViewColumn>) =>
    columns
        .map(
            (column, index) =>
                column.width ??
                (index === 0 ? 'minmax(140px, 2fr)' : 'minmax(72px, 1fr)'),
        )
        .join(' ');

export function ListView({
    view = 'details',
    items,
    columns,
    selectionMode = 'single',
    selected,
    defaultSelected,
    onSelectionChange,
    onActivate,
    activateOn = 'double',
    tileSize = 'regular',
    sort,
    onSortChange,
    label,
    empty,
    className,
    style,
    onKeyDown,
    ref,
    ...rest
}: ListViewProps) {
    const root = useRef<HTMLDivElement | null>(null);
    const [own, setOwn] = useState<ReadonlyArray<string>>(
        defaultSelected ?? [],
    );
    const chosen = new Set(selected ?? own);
    const [focused, setFocused] = useState<string | undefined>();
    const [moved, setMoved] = useState(false);
    const [anchor, setAnchor] = useState<string | undefined>();
    const enabled = items.filter((item) => !item.disabled);
    const focusedItem =
        items.find((item) => item.id === focused && !item.disabled) ??
        undefined;
    const isDetails = view === 'details';
    const cols: ReadonlyArray<ListViewColumn> =
        isDetails && columns && columns.length > 0
            ? columns
            : [{ key: 'label', header: label ?? '' }];

    const tabStop =
        focusedItem?.id ??
        enabled.find((item) => chosen.has(item.id))?.id ??
        enabled[0]?.id;

    useEffect(() => {
        if (!moved || !focused) return;
        setMoved(false);
        root.current
            ?.querySelector<HTMLElement>(
                `[data-lv-item="${CSS.escape(focused)}"]`,
            )
            ?.focus();
    }, [moved, focused]);

    const commit = (next: Iterable<string>) => {
        const list = Array.from(next);
        setOwn(list);
        onSelectionChange?.(list);
    };

    const range = (from: string | undefined, to: string) => {
        const ids = enabled.map((item) => item.id);
        const a = from ? ids.indexOf(from) : -1;
        const b = ids.indexOf(to);
        if (a < 0) return [to];
        return ids.slice(Math.min(a, b), Math.max(a, b) + 1);
    };

    const choose = (
        id: string,
        mode: { toggle?: boolean; extend?: boolean; moveOnly?: boolean },
    ) => {
        setFocused(id);
        setMoved(true);
        if (selectionMode === 'none' || mode.moveOnly) return;
        if (selectionMode === 'single') {
            setAnchor(id);
            commit([id]);
            return;
        }
        if (mode.extend) {
            commit(range(anchor, id));
            return;
        }
        setAnchor(id);
        if (mode.toggle) {
            const next = new Set(chosen);
            if (next.has(id)) next.delete(id);
            else next.add(id);
            commit(next);
            return;
        }
        commit([id]);
    };

    const elements = () =>
        Array.from(
            root.current?.querySelectorAll<HTMLElement>(
                '[data-lv-item]:not([aria-disabled="true"])',
            ) ?? [],
        );

    const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        onKeyDown?.(event);
        if (event.defaultPrevented || enabled.length === 0) return;
        const current = tabStop;
        const multi = selectionMode === 'multiple';
        const flags = {
            extend: multi && event.shiftKey,
            moveOnly: multi && (event.ctrlKey || event.metaKey),
        };
        const direction = arrows[event.key];
        if (direction) {
            event.preventDefault();
            if (!current) {
                choose(enabled[0].id, {});
                return;
            }
            const nodes = elements();
            const from = nodes.findIndex(
                (node) => node.dataset.lvItem === current,
            );
            const to = neighbour(
                nodes.map((node) => node.getBoundingClientRect()),
                from,
                direction,
            );
            const id = to === undefined ? undefined : nodes[to].dataset.lvItem;
            if (id) choose(id, flags);
            return;
        }
        if (event.key === 'Home' || event.key === 'End') {
            event.preventDefault();
            const target =
                event.key === 'Home' ? enabled[0] : enabled[enabled.length - 1];
            choose(target.id, flags);
            return;
        }
        if (event.key === ' ' && current) {
            event.preventDefault();
            choose(current, {
                toggle: multi && (event.ctrlKey || event.metaKey),
            });
            return;
        }
        if (event.key === 'Enter' && current) {
            event.preventDefault();
            onActivate?.(current);
            return;
        }
        if (
            multi &&
            (event.ctrlKey || event.metaKey) &&
            event.key.toLowerCase() === 'a'
        ) {
            event.preventDefault();
            commit(enabled.map((item) => item.id));
        }
    };

    const itemAt = (target: EventTarget) => {
        const id = (target as Element).closest<HTMLElement>('[data-lv-item]')
            ?.dataset.lvItem;
        return enabled.find((item) => item.id === id);
    };

    const handleClick = (event: MouseEvent<HTMLDivElement>) => {
        const item = itemAt(event.target);
        if (!item) return;
        const multi = selectionMode === 'multiple';
        const modified = event.ctrlKey || event.metaKey || event.shiftKey;
        choose(item.id, {
            toggle: multi && (event.ctrlKey || event.metaKey),
            extend: multi && event.shiftKey,
        });
        if (activateOn === 'single' && !modified) onActivate?.(item.id);
    };

    const sortBy = (column: ListViewColumn) => {
        if (!onSortChange) return;
        const direction =
            sort?.key === column.key && sort.direction === 'ascending'
                ? 'descending'
                : 'ascending';
        onSortChange({ key: column.key, direction });
    };

    const name = (item: ListViewItem, size: 'small' | 'large') => {
        const icon =
            size === 'large' ? (item.largeIcon ?? item.icon) : item.icon;
        return (
            <>
                {icon && (
                    <span className="ui-listview-icon" aria-hidden="true">
                        {icon}
                    </span>
                )}
                <span className="ui-listview-text">
                    <span className="ui-listview-label">{item.label}</span>
                    {view === 'tiles' &&
                        item.details?.map((line, index) => (
                            <span
                                // biome-ignore lint/suspicious/noArrayIndexKey: detail lines are positional
                                key={index}
                                className="ui-listview-detail"
                            >
                                {line}
                            </span>
                        ))}
                </span>
            </>
        );
    };

    const itemProps = (item: ListViewItem) => ({
        'data-lv-item': item.id,
        onFocus: () => setFocused(item.id),
        'aria-selected':
            selectionMode === 'none' ? undefined : chosen.has(item.id),
        'aria-disabled': item.disabled || undefined,
        'data-tone': item.tone,
    });

    const shared = {
        ref: (node: HTMLDivElement | null) => {
            root.current = node;
            if (typeof ref === 'function') return ref(node);
            if (ref) ref.current = node;
        },
        'aria-label': label,
        'aria-multiselectable': selectionMode === 'multiple' || undefined,
        className: className ? `ui-listview ${className}` : 'ui-listview',
        'data-view': view,
        'data-tile-size':
            view === 'tiles' && tileSize === 'compact' ? 'compact' : undefined,
        'data-activate': activateOn === 'single' ? 'single' : undefined,
        'data-empty': items.length === 0 ? '' : undefined,
        onKeyDown: handleKeyDown,
        onClick: handleClick,
        onDoubleClick: (event: MouseEvent<HTMLDivElement>) => {
            if (activateOn === 'single') return;
            const item = itemAt(event.target);
            if (item) onActivate?.(item.id);
        },
    };

    if (isDetails)
        return (
            // biome-ignore lint/a11y/useSemanticElements: a ListView in details view is an ARIA grid with row focus, like a Windows list view
            <div
                role="grid"
                aria-rowcount={items.length + 1}
                aria-colcount={cols.length}
                data-wrap={cols.some((column) => column.wrap) ? '' : undefined}
                style={
                    {
                        ...style,
                        '--ui-listview-columns': columnTemplate(cols),
                    } as CSSProperties
                }
                {...shared}
                {...rest}
            >
                {/* biome-ignore lint/a11y/useSemanticElements: a ListView in details view is an ARIA grid with row focus, like a Windows list view */}
                {/* biome-ignore lint/a11y/useFocusableInteractive: the header row is not a stop; its sort buttons are */}
                <div
                    role="row"
                    className="ui-listview-header"
                    aria-rowindex={1}
                >
                    {cols.map((column) => {
                        const sorted = sort?.key === column.key;
                        const canSort = column.sortable && onSortChange;
                        return (
                            // biome-ignore lint/a11y/useSemanticElements: header cell of the ListView grid
                            // biome-ignore lint/a11y/useFocusableInteractive: its sort button is the interactive part
                            <div
                                key={column.key}
                                role="columnheader"
                                className="ui-listview-columnheader"
                                aria-sort={sorted ? sort.direction : undefined}
                                data-align={column.align}
                                data-wrap={column.wrap ? '' : undefined}
                                data-sorted={
                                    sorted ? sort.direction : undefined
                                }
                            >
                                {canSort ? (
                                    <button
                                        type="button"
                                        className="ui-listview-sort"
                                        onClick={() => sortBy(column)}
                                    >
                                        <span className="ui-listview-header-text">
                                            {column.header}
                                        </span>
                                        <span
                                            className="ui-listview-sort-arrow"
                                            aria-hidden="true"
                                        />
                                    </button>
                                ) : (
                                    <span className="ui-listview-sort">
                                        <span className="ui-listview-header-text">
                                            {column.header}
                                        </span>
                                    </span>
                                )}
                            </div>
                        );
                    })}
                </div>
                {items.map((item, index) => (
                    // biome-ignore lint/a11y/useSemanticElements: a ListView in details view is an ARIA grid with row focus, like a Windows list view
                    <div
                        key={item.id}
                        role="row"
                        tabIndex={item.id === tabStop ? 0 : -1}
                        aria-rowindex={index + 2}
                        className="ui-listview-item"
                        {...itemProps(item)}
                    >
                        {cols.map((column, col) => (
                            // biome-ignore lint/a11y/useSemanticElements: cell of a ListView row
                            // biome-ignore lint/a11y/useFocusableInteractive: rows take focus, cells do not
                            <div
                                key={column.key}
                                role="gridcell"
                                className="ui-listview-cell"
                                data-align={column.align}
                                data-wrap={column.wrap ? '' : undefined}
                            >
                                {col === 0
                                    ? name(item, 'small')
                                    : item.cells?.[column.key]}
                            </div>
                        ))}
                    </div>
                ))}
                {items.length === 0 && empty && (
                    <div className="ui-listview-empty">{empty}</div>
                )}
            </div>
        );

    return (
        <div role="listbox" style={style} {...shared} {...rest}>
            {items.map((item) => (
                <div
                    key={item.id}
                    role="option"
                    tabIndex={item.id === tabStop ? 0 : -1}
                    className="ui-listview-item"
                    {...itemProps(item)}
                >
                    {name(item, view === 'list' ? 'small' : 'large')}
                </div>
            ))}
            {items.length === 0 && empty && (
                <div className="ui-listview-empty">{empty}</div>
            )}
        </div>
    );
}
