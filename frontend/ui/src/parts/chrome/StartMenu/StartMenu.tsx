import {
    type ComponentPropsWithRef,
    type KeyboardEvent,
    type ReactNode,
    useEffect,
    useRef,
} from 'react';
import type { Tone } from '../../controls/tone';
import { Glyph } from '../glyphs';

export type StartMenuProps = Omit<ComponentPropsWithRef<'div'>, 'children'> & {
    user: ReactNode;
    picture?: ReactNode;
    programs: ReactNode;
    places?: ReactNode;
    footer?: ReactNode;
    onClose?: () => void;
    label?: string;
};

const itemSelector = '[role="menuitem"]:not(:disabled)';

const itemsOf = (root: Element | null) =>
    root ? Array.from(root.querySelectorAll<HTMLElement>(itemSelector)) : [];

const groupOf = (element: Element) =>
    element.closest<HTMLElement>('[data-ui-start-group]');

function navigate(event: KeyboardEvent<HTMLDivElement>) {
    const root = event.currentTarget;
    const all = itemsOf(root);
    const current = document.activeElement as HTMLElement | null;
    const at = current ? all.indexOf(current) : -1;
    const go = (target: HTMLElement | undefined) => {
        if (!target) return;
        event.preventDefault();
        target.focus();
    };
    switch (event.key) {
        case 'ArrowDown':
            go(all[(at + 1) % all.length]);
            return;
        case 'ArrowUp':
            go(all[(at - 1 + all.length) % all.length]);
            return;
        case 'Home':
            go(all[0]);
            return;
        case 'End':
            go(all[all.length - 1]);
            return;
        case 'ArrowLeft':
        case 'ArrowRight': {
            if (!current) return;
            const group = groupOf(current);
            if (!group) return;
            const step = event.key === 'ArrowRight' ? 1 : -1;
            if (group.dataset.uiStartGroup === 'footer') {
                const footer = itemsOf(group);
                const index = footer.indexOf(current);
                go(footer[(index + step + footer.length) % footer.length]);
                return;
            }
            const columns = Array.from(
                root.querySelectorAll<HTMLElement>(
                    '[data-ui-start-group="column"]',
                ),
            ).filter((column) => itemsOf(column).length > 0);
            const column = columns.indexOf(group);
            const other = columns[column + step];
            if (!other) return;
            const mine = itemsOf(group);
            const theirs = itemsOf(other);
            go(theirs[Math.min(mine.indexOf(current), theirs.length - 1)]);
            return;
        }
    }
}

export function StartMenu({
    user,
    picture,
    programs,
    places,
    footer,
    onClose,
    label = 'Start menu',
    className,
    onKeyDown,
    ...rest
}: StartMenuProps) {
    const rootRef = useRef<HTMLDivElement>(null);
    const close = useRef(onClose);
    close.current = onClose;

    useEffect(() => {
        itemsOf(rootRef.current)[0]?.focus({ preventScroll: true });
        const onPointer = (event: PointerEvent) => {
            const target = event.target;
            if (!(target instanceof Element)) return;
            if (rootRef.current?.contains(target)) return;
            if (target.closest('[data-ui-start-button]')) return;
            close.current?.();
        };
        document.addEventListener('pointerdown', onPointer, true);
        return () =>
            document.removeEventListener('pointerdown', onPointer, true);
    }, []);

    return (
        <div
            ref={rootRef}
            role="menu"
            aria-label={label}
            className={
                className ? `ui-start-menu ${className}` : 'ui-start-menu'
            }
            onKeyDown={(event) => {
                onKeyDown?.(event);
                if (event.defaultPrevented) return;
                if (event.key === 'Escape' && close.current) {
                    event.preventDefault();
                    close.current();
                    document
                        .querySelector<HTMLElement>('[data-ui-start-button]')
                        ?.focus();
                    return;
                }
                navigate(event);
            }}
            {...rest}
        >
            <div className="ui-start-menu-header" role="presentation">
                {picture && (
                    <span className="ui-start-menu-picture" aria-hidden="true">
                        {picture}
                    </span>
                )}
                <span className="ui-start-menu-user">{user}</span>
            </div>
            <div className="ui-start-menu-columns" role="presentation">
                {/* biome-ignore lint/a11y/useSemanticElements: a group of menu items inside a menu, not a form fieldset */}
                <div
                    role="group"
                    className="ui-start-menu-programs"
                    data-ui-start-group="column"
                >
                    {programs}
                </div>
                {places && (
                    // biome-ignore lint/a11y/useSemanticElements: a group of menu items inside a menu, not a form fieldset
                    <div
                        role="group"
                        className="ui-start-menu-places"
                        data-ui-start-group="column"
                    >
                        {places}
                    </div>
                )}
            </div>
            {footer && (
                // biome-ignore lint/a11y/useSemanticElements: a group of menu items inside a menu, not a form fieldset
                <div
                    role="group"
                    className="ui-start-menu-footer"
                    data-ui-start-group="footer"
                >
                    {footer}
                </div>
            )}
        </div>
    );
}

export type StartMenuItemProps = Omit<
    ComponentPropsWithRef<'button'>,
    'children'
> & {
    label: ReactNode;
    icon?: ReactNode;
    description?: ReactNode;
};

export function StartMenuItem({
    label,
    icon,
    description,
    className,
    ...rest
}: StartMenuItemProps) {
    return (
        <button
            type="button"
            role="menuitem"
            tabIndex={-1}
            className={
                className
                    ? `ui-start-menu-item ${className}`
                    : 'ui-start-menu-item'
            }
            {...rest}
        >
            <span className="ui-start-menu-item-icon" aria-hidden="true">
                {icon}
            </span>
            <span className="ui-start-menu-item-text">
                <span className="ui-start-menu-item-label">{label}</span>
                {description && (
                    <span className="ui-start-menu-item-description">
                        {description}
                    </span>
                )}
            </span>
        </button>
    );
}

export function StartMenuSeparator() {
    return <hr className="ui-start-menu-separator" />;
}

export type StartMenuActionProps = ComponentPropsWithRef<'button'> & {
    tone?: Tone;
    icon?: ReactNode;
};

const toneGlyph = (tone: Tone) =>
    tone === 'danger' ? (
        <Glyph name="power" />
    ) : tone === 'caution' ? (
        <Glyph name="log-off" />
    ) : null;

export function StartMenuAction({
    tone = 'neutral',
    icon,
    className,
    children,
    ...rest
}: StartMenuActionProps) {
    return (
        <button
            type="button"
            role="menuitem"
            tabIndex={-1}
            className={
                className
                    ? `ui-start-menu-action ${className}`
                    : 'ui-start-menu-action'
            }
            data-tone={tone}
            {...rest}
        >
            <span className="ui-start-menu-action-icon" aria-hidden="true">
                {icon ?? toneGlyph(tone)}
            </span>
            <span>{children}</span>
        </button>
    );
}
