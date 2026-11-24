import {
    type ComponentPropsWithRef,
    type KeyboardEvent,
    type MouseEvent,
    type ReactNode,
    useEffect,
    useRef,
    useState,
} from 'react';
import { Expander } from '../../surfaces/Expander/Expander';
import { ContactRow } from '../ContactRow/ContactRow';

export type BuddyListItem = {
    id: string;
    name: ReactNode;
    picture?: ReactNode;
    meta?: ReactNode;
    details?: ReactNode;
    title?: string;
    unread?: boolean;
    dimmed?: boolean;
    disabled?: boolean;
};

export type BuddyListGroup = {
    id: string;
    title: string;
    count?: number;
    items: ReadonlyArray<BuddyListItem>;
    empty?: ReactNode;
};

export type BuddyListPoint = { x: number; y: number };

export type BuddyListProps = Omit<
    ComponentPropsWithRef<'div'>,
    'children' | 'onSelect'
> & {
    groups: ReadonlyArray<BuddyListGroup>;
    selectionMode?: 'single' | 'multiple';
    selected?: ReadonlyArray<string>;
    defaultSelected?: ReadonlyArray<string>;
    onSelectionChange?: (selected: string[]) => void;
    onActivate?: (id: string) => void;
    onItemContextMenu?: (
        id: string,
        at: BuddyListPoint,
        target: HTMLElement,
    ) => void;
    collapsed?: ReadonlyArray<string>;
    onCollapsedChange?: (collapsed: string[]) => void;
    children?: ReactNode;
};

export function BuddyList({
    groups,
    selectionMode = 'single',
    selected,
    defaultSelected,
    onSelectionChange,
    onActivate,
    onItemContextMenu,
    collapsed,
    onCollapsedChange,
    className,
    onKeyDown,
    children,
    ref,
    ...rest
}: BuddyListProps) {
    const root = useRef<HTMLDivElement | null>(null);
    const [ownSelected, setOwnSelected] = useState<ReadonlyArray<string>>(
        defaultSelected ?? [],
    );
    const [ownCollapsed, setOwnCollapsed] = useState<ReadonlyArray<string>>([]);
    const [focused, setFocused] = useState<string | undefined>();
    const [anchor, setAnchor] = useState<string | undefined>();
    const [moved, setMoved] = useState(false);
    const chosen = new Set(selected ?? ownSelected);
    const shut = new Set(collapsed ?? ownCollapsed);
    const multi = selectionMode === 'multiple';

    const visible = groups
        .filter((group) => !shut.has(group.id))
        .flatMap((group) => group.items.filter((item) => !item.disabled));
    const ids = visible.map((item) => item.id);
    const tabStop =
        (focused && ids.includes(focused) ? focused : undefined) ??
        ids.find((id) => chosen.has(id)) ??
        ids[0];

    useEffect(() => {
        if (!moved || !focused) return;
        setMoved(false);
        root.current
            ?.querySelector<HTMLElement>(
                `[data-buddy-item="${CSS.escape(focused)}"]`,
            )
            ?.focus();
    }, [moved, focused]);

    const commit = (next: Iterable<string>) => {
        const list = Array.from(next);
        setOwnSelected(list);
        onSelectionChange?.(list);
    };

    const range = (from: string | undefined, to: string) => {
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
        if (mode.moveOnly) return;
        if (multi && mode.extend) {
            commit(range(anchor, id));
            return;
        }
        setAnchor(id);
        if (multi && mode.toggle) {
            const next = new Set(chosen);
            if (next.has(id)) next.delete(id);
            else next.add(id);
            commit(next);
            return;
        }
        commit([id]);
    };

    const setGroupOpen = (groupId: string, open: boolean) => {
        const next = new Set(shut);
        if (open) next.delete(groupId);
        else next.add(groupId);
        const list = Array.from(next);
        setOwnCollapsed(list);
        onCollapsedChange?.(list);
    };

    const itemOf = (target: EventTarget) =>
        (target as Element).closest<HTMLElement>('[data-buddy-item]');

    const menuAt = (id: string, at: BuddyListPoint, element: HTMLElement) => {
        if (!chosen.has(id)) choose(id, {});
        onItemContextMenu?.(id, at, element);
    };

    const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        onKeyDown?.(event);
        if (event.defaultPrevented) return;
        const element = itemOf(event.target);
        const current = element?.dataset.buddyItem;
        if (!element || !current || ids.length === 0) return;
        const ctrl = event.ctrlKey || event.metaKey;
        const flags = {
            extend: multi && event.shiftKey,
            moveOnly: multi && ctrl,
        };
        const index = ids.indexOf(current);
        const step =
            event.key === 'ArrowDown' ? 1 : event.key === 'ArrowUp' ? -1 : 0;
        if (step) {
            event.preventDefault();
            const next =
                ids[Math.min(Math.max(index + step, 0), ids.length - 1)];
            if (next) choose(next, flags);
            return;
        }
        if (event.key === 'Home' || event.key === 'End') {
            event.preventDefault();
            choose(event.key === 'Home' ? ids[0] : ids[ids.length - 1], flags);
            return;
        }
        if (event.key === ' ') {
            event.preventDefault();
            choose(current, { toggle: ctrl });
            return;
        }
        if (event.key === 'Enter') {
            event.preventDefault();
            onActivate?.(current);
            return;
        }
        if (multi && ctrl && event.key.toLowerCase() === 'a') {
            event.preventDefault();
            commit(ids);
            return;
        }
        if (
            onItemContextMenu &&
            (event.key === 'ContextMenu' ||
                (event.shiftKey && event.key === 'F10'))
        ) {
            event.preventDefault();
            const r = element.getBoundingClientRect();
            menuAt(current, { x: r.left + 16, y: r.bottom }, element);
        }
    };

    const handleClick = (event: MouseEvent<HTMLDivElement>) => {
        const element = itemOf(event.target);
        const id = element?.dataset.buddyItem;
        if (!id || !ids.includes(id)) return;
        choose(id, {
            toggle: event.ctrlKey || event.metaKey,
            extend: event.shiftKey,
        });
    };

    return (
        // biome-ignore lint/a11y/noStaticElementInteractions: keys and clicks reach the options inside, which are the interactive parts
        <div
            ref={(node) => {
                root.current = node;
                if (typeof ref === 'function') return ref(node);
                if (ref) ref.current = node;
            }}
            className={
                className ? `ui-buddy-list ${className}` : 'ui-buddy-list'
            }
            onKeyDown={handleKeyDown}
            onClick={handleClick}
            onDoubleClick={(event) => {
                const id = itemOf(event.target)?.dataset.buddyItem;
                if (id && ids.includes(id)) onActivate?.(id);
            }}
            onContextMenu={(event) => {
                const element = itemOf(event.target);
                const id = element?.dataset.buddyItem;
                if (!element || !id || !onItemContextMenu || !ids.includes(id))
                    return;
                event.preventDefault();
                menuAt(id, { x: event.clientX, y: event.clientY }, element);
            }}
            {...rest}
        >
            {groups.map((group) => (
                <Expander
                    key={group.id}
                    title={group.title}
                    count={group.count ?? group.items.length}
                    open={!shut.has(group.id)}
                    onOpenChange={(open) => setGroupOpen(group.id, open)}
                >
                    {group.items.length > 0 ? (
                        <div
                            role="listbox"
                            aria-label={group.title}
                            aria-multiselectable={multi || undefined}
                            className="ui-buddy-list-group"
                        >
                            {group.items.map((item) => (
                                <ContactRow
                                    key={item.id}
                                    role="option"
                                    className="ui-buddy-list-item"
                                    data-buddy-item={item.id}
                                    tabIndex={item.id === tabStop ? 0 : -1}
                                    aria-selected={chosen.has(item.id)}
                                    aria-disabled={item.disabled || undefined}
                                    onFocus={() => setFocused(item.id)}
                                    title={item.title}
                                    selected={chosen.has(item.id)}
                                    unread={item.unread}
                                    dimmed={item.dimmed}
                                    picture={item.picture}
                                    name={item.name}
                                    meta={item.meta}
                                    details={item.details}
                                />
                            ))}
                        </div>
                    ) : (
                        group.empty && (
                            <div className="ui-buddy-list-empty">
                                {group.empty}
                            </div>
                        )
                    )}
                </Expander>
            ))}
            {children}
        </div>
    );
}
