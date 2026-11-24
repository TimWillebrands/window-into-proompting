import type { ComponentPropsWithRef, KeyboardEvent, ReactNode } from 'react';
import { ContextMenu, useContextMenu } from '../ContextMenu/ContextMenu';
import type { MenuEntry } from '../ContextMenu/menu';
import { Glyph } from '../glyphs';

export type TaskbarProps = ComponentPropsWithRef<'div'> & {
    start?: ReactNode;
    tray?: ReactNode;
};

export const moveFocus = (event: KeyboardEvent<HTMLDivElement>) => {
    const steps: Record<string, number> = {
        ArrowRight: 1,
        ArrowDown: 1,
        ArrowLeft: -1,
        ArrowUp: -1,
    };
    const step = steps[event.key];
    const edge = event.key === 'Home' ? 0 : event.key === 'End' ? -1 : null;
    if (step === undefined && edge === null) return;
    const buttons = Array.from(
        event.currentTarget.querySelectorAll<HTMLButtonElement>(
            'button:not(:disabled)',
        ),
    );
    if (buttons.length === 0) return;
    event.preventDefault();
    const at = buttons.indexOf(document.activeElement as HTMLButtonElement);
    const next =
        edge === 0
            ? 0
            : edge === -1
              ? buttons.length - 1
              : (at + (step ?? 0) + buttons.length) % buttons.length;
    buttons[next]?.focus();
};

export function Taskbar({
    start,
    tray,
    className,
    children,
    ...rest
}: TaskbarProps) {
    return (
        <div
            className={className ? `ui-taskbar ${className}` : 'ui-taskbar'}
            {...rest}
        >
            {start}
            <div
                role="toolbar"
                aria-label="Open windows"
                className="ui-taskbar-tasks"
                onKeyDown={moveFocus}
            >
                {children}
            </div>
            {tray}
        </div>
    );
}

export type TaskbarButtonProps = Omit<
    ComponentPropsWithRef<'button'>,
    'children'
> & {
    taskId: string;
    label: ReactNode;
    icon?: ReactNode;
    active?: boolean;
    minimised?: boolean;
    maximised?: boolean;
    onRestore?: () => void;
    onMinimise?: () => void;
    onMaximise?: () => void;
    onClose?: () => void;
};

const opensMenu = (event: KeyboardEvent<HTMLButtonElement>) =>
    event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10');

export function TaskbarButton({
    taskId,
    label,
    icon,
    active = false,
    minimised = false,
    maximised = false,
    onRestore,
    onMinimise,
    onMaximise,
    onClose,
    className,
    onContextMenu,
    onKeyDown,
    ...rest
}: TaskbarButtonProps) {
    const menu = useContextMenu();
    const hasMenu = Boolean(onRestore || onMinimise || onMaximise || onClose);
    const items: MenuEntry[] = [
        {
            id: 'restore',
            label: 'Restore',
            icon: <Glyph name="restore" />,
            disabled: !onRestore || !(minimised || maximised),
            onSelect: onRestore,
        },
        {
            id: 'minimise',
            label: 'Minimise',
            icon: <Glyph name="minimise" />,
            disabled: !onMinimise || minimised,
            onSelect: onMinimise,
        },
        {
            id: 'maximise',
            label: 'Maximise',
            icon: <Glyph name="maximise" />,
            disabled: !onMaximise || maximised,
            onSelect: onMaximise,
        },
        { type: 'separator', id: 'close-separator' },
        {
            id: 'close',
            label: 'Close',
            icon: <Glyph name="close" />,
            disabled: !onClose,
            onSelect: onClose,
        },
    ];
    return (
        <>
            <button
                type="button"
                className={
                    className
                        ? `ui-taskbar-button ${className}`
                        : 'ui-taskbar-button'
                }
                aria-pressed={active}
                data-active={active ? '' : undefined}
                data-ui-task-id={taskId}
                onContextMenu={(event) => {
                    onContextMenu?.(event);
                    if (hasMenu && !event.defaultPrevented)
                        menu.onContextMenu(event);
                }}
                onKeyDown={(event) => {
                    onKeyDown?.(event);
                    if (!hasMenu || event.defaultPrevented || !opensMenu(event))
                        return;
                    event.preventDefault();
                    const rect = event.currentTarget.getBoundingClientRect();
                    menu.openAt(
                        { x: rect.left, y: rect.top },
                        event.currentTarget,
                    );
                }}
                {...rest}
            >
                {icon && (
                    <span className="ui-taskbar-button-icon" aria-hidden="true">
                        {icon}
                    </span>
                )}
                <span className="ui-taskbar-button-label">{label}</span>
            </button>
            {hasMenu && (
                <ContextMenu
                    {...menu.menuProps}
                    items={items}
                    label={typeof label === 'string' ? label : undefined}
                />
            )}
        </>
    );
}
