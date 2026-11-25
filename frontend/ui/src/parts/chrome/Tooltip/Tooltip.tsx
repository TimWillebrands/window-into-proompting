import {
    type CSSProperties,
    cloneElement,
    type FocusEvent,
    type KeyboardEvent,
    type PointerEvent,
    type ReactElement,
    type ReactNode,
    useEffect,
    useId,
    useLayoutEffect,
    useRef,
    useState,
} from 'react';
import { type Placed, placeAround } from '../placement';
import { ChromePortal } from '../portal';

type TriggerProps = {
    'aria-describedby'?: string;
    onPointerEnter?: (event: PointerEvent<Element>) => void;
    onPointerLeave?: (event: PointerEvent<Element>) => void;
    onFocus?: (event: FocusEvent<Element>) => void;
    onBlur?: (event: FocusEvent<Element>) => void;
    onKeyDown?: (event: KeyboardEvent<Element>) => void;
};

export type TooltipProps = {
    content: ReactNode;
    children: ReactElement<TriggerProps>;
    placement?: 'above' | 'below';
    delay?: number;
    open?: boolean;
};

export function TooltipBubble({
    id,
    anchor,
    placement,
    children,
}: {
    id: string;
    anchor: Element;
    placement: 'above' | 'below';
    children: ReactNode;
}) {
    const [element, setElement] = useState<HTMLDivElement | null>(null);
    const [placed, setPlaced] = useState<Placed | null>(null);
    useLayoutEffect(() => {
        if (!element) return;
        const next = placeAround(
            anchor.getBoundingClientRect(),
            { width: element.offsetWidth, height: element.offsetHeight },
            placement,
            6,
        );
        setPlaced((previous) =>
            previous && previous.x === next.x && previous.y === next.y
                ? previous
                : next,
        );
    });
    const style = {
        left: placed?.x ?? 0,
        top: placed?.y ?? 0,
        visibility: placed ? undefined : 'hidden',
    } as CSSProperties;
    return (
        <ChromePortal owner={anchor}>
            <div
                ref={setElement}
                id={id}
                role="tooltip"
                className="ui-tooltip"
                data-placement={placed?.side ?? placement}
                data-placed={placed ? '' : undefined}
                style={style}
            >
                {children}
            </div>
        </ChromePortal>
    );
}

export function Tooltip({
    content,
    children,
    placement = 'below',
    delay = 500,
    open: forced,
}: TooltipProps) {
    const id = useId();
    const [anchor, setAnchor] = useState<Element | null>(null);
    const [shown, setShown] = useState(false);
    const timer = useRef<number | undefined>(undefined);

    useEffect(() => () => window.clearTimeout(timer.current), []);

    const show = (target: Element, wait: number) => {
        setAnchor(target);
        window.clearTimeout(timer.current);
        if (wait <= 0) setShown(true);
        else timer.current = window.setTimeout(() => setShown(true), wait);
    };
    const hide = () => {
        window.clearTimeout(timer.current);
        setShown(false);
    };

    const visible = forced ?? shown;
    const own = children.props;
    const trigger = cloneElement(children, {
        'aria-describedby': visible
            ? [own['aria-describedby'], id].filter(Boolean).join(' ')
            : own['aria-describedby'],
        onPointerEnter: (event) => {
            own.onPointerEnter?.(event);
            show(event.currentTarget, delay);
        },
        onPointerLeave: (event) => {
            own.onPointerLeave?.(event);
            hide();
        },
        onFocus: (event) => {
            own.onFocus?.(event);
            if (event.currentTarget.matches(':focus-visible'))
                show(event.currentTarget, 0);
        },
        onBlur: (event) => {
            own.onBlur?.(event);
            hide();
        },
        onKeyDown: (event) => {
            own.onKeyDown?.(event);
            if (event.key === 'Escape') hide();
        },
    });

    return (
        <>
            {trigger}
            {visible && anchor && (
                <TooltipBubble id={id} anchor={anchor} placement={placement}>
                    {content}
                </TooltipBubble>
            )}
            {forced && !anchor && <TooltipAnchorProbe onAnchor={setAnchor} />}
        </>
    );
}

function TooltipAnchorProbe({
    onAnchor,
}: {
    onAnchor: (element: Element | null) => void;
}) {
    const [probe, setProbe] = useState<HTMLSpanElement | null>(null);
    useLayoutEffect(() => {
        onAnchor(probe?.previousElementSibling ?? null);
    }, [probe, onAnchor]);
    return <span ref={setProbe} hidden />;
}
