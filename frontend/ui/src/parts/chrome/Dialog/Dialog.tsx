import {
    type KeyboardEvent,
    type ReactNode,
    type RefObject,
    useEffect,
    useRef,
    useState,
} from 'react';
import { Window, type WindowProps } from '../Window/Window';

export type DialogProps = Omit<
    WindowProps,
    'onMinimise' | 'onMaximise' | 'maximised' | 'active' | 'onClose'
> & {
    onClose: () => void;
    buttons?: ReactNode;
    initialFocus?: RefObject<HTMLElement | null>;
    contained?: boolean;
    overlayClassName?: string;
};

const tabbable =
    'a[href], area[href], button:not(:disabled), input:not(:disabled):not([type="hidden"]), select:not(:disabled), textarea:not(:disabled), iframe, [contenteditable="true"], [tabindex]:not([tabindex="-1"])';

const tabbablesIn = (root: HTMLElement) =>
    Array.from(root.querySelectorAll<HTMLElement>(tabbable)).filter(
        (element) =>
            !element.closest('[inert]') && element.getClientRects().length > 0,
    );

const firstFocus = (root: HTMLElement) => {
    const content = root.querySelector<HTMLElement>('.ui-window-body');
    const inContent = content ? tabbablesIn(content) : [];
    return inContent[0] ?? tabbablesIn(root)[0] ?? root;
};

export function Dialog({
    onClose,
    buttons,
    initialFocus,
    contained = false,
    overlayClassName,
    className,
    children,
    onKeyDown,
    ...rest
}: DialogProps) {
    const windowRef = useRef<HTMLElement>(null);
    const [flashes, setFlashes] = useState(0);

    useEffect(() => {
        const previous =
            document.activeElement instanceof HTMLElement
                ? document.activeElement
                : null;
        const root = windowRef.current;
        if (root) (initialFocus?.current ?? firstFocus(root)).focus();
        return () => {
            if (previous?.isConnected) previous.focus();
        };
    }, [initialFocus]);

    const trap = (event: KeyboardEvent<HTMLElement>) => {
        onKeyDown?.(event);
        if (event.defaultPrevented) return;
        if (event.key === 'Escape') {
            event.stopPropagation();
            onClose();
            return;
        }
        if (event.key !== 'Tab') return;
        const root = windowRef.current;
        if (!root) return;
        const items = tabbablesIn(root);
        if (items.length === 0) {
            event.preventDefault();
            return;
        }
        const first = items[0];
        const last = items[items.length - 1];
        const active = document.activeElement;
        if (event.shiftKey && (active === first || active === root)) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && active === last) {
            event.preventDefault();
            first.focus();
        }
    };

    return (
        <div
            role="presentation"
            className={
                overlayClassName
                    ? `ui-dialog-overlay ${overlayClassName}`
                    : 'ui-dialog-overlay'
            }
            data-contained={contained ? '' : undefined}
            onPointerDown={(event) => {
                if (event.target === event.currentTarget)
                    setFlashes((n) => n + 1);
            }}
        >
            <Window
                ref={windowRef}
                role="dialog"
                aria-modal="true"
                tabIndex={-1}
                className={className ? `ui-dialog ${className}` : 'ui-dialog'}
                data-flash={
                    flashes % 2 === 1 ? 'odd' : flashes ? 'even' : undefined
                }
                onClose={onClose}
                onKeyDown={trap}
                {...rest}
            >
                <div className="ui-dialog-content">{children}</div>
                {buttons && <div className="ui-dialog-buttons">{buttons}</div>}
            </Window>
        </div>
    );
}
