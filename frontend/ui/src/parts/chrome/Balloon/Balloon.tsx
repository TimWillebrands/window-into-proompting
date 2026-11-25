import {
    type ComponentPropsWithoutRef,
    type CSSProperties,
    type ReactNode,
    useEffect,
    useId,
    useLayoutEffect,
    useState,
} from 'react';
import { Glyph } from '../glyphs';
import { type Placed, placeAround } from '../placement';
import { ChromePortal } from '../portal';

export type BalloonProps = Omit<ComponentPropsWithoutRef<'div'>, 'title'> & {
    title?: ReactNode;
    icon?: ReactNode;
    onClose?: () => void;
    placement?: 'above' | 'below';
    tailAlign?: 'start' | 'center' | 'end';
    anchor?: Element | null;
};

const tailGap = 12;

function useAnchoredPlace(
    anchor: Element | null | undefined,
    balloon: HTMLElement | null,
    placement: 'above' | 'below',
) {
    const [placed, setPlaced] = useState<Placed | null>(null);
    const [, setTick] = useState(0);
    useEffect(() => {
        if (!anchor) return;
        const bump = () => setTick((n) => n + 1);
        window.addEventListener('resize', bump);
        window.addEventListener('scroll', bump, true);
        return () => {
            window.removeEventListener('resize', bump);
            window.removeEventListener('scroll', bump, true);
        };
    }, [anchor]);
    useLayoutEffect(() => {
        if (!anchor || !balloon) return;
        const next = placeAround(
            anchor.getBoundingClientRect(),
            { width: balloon.offsetWidth, height: balloon.offsetHeight },
            placement,
            tailGap,
        );
        setPlaced((previous) =>
            previous &&
            previous.x === next.x &&
            previous.y === next.y &&
            previous.side === next.side &&
            previous.tip === next.tip
                ? previous
                : next,
        );
    });
    return placed;
}

export function Balloon({
    title,
    icon,
    onClose,
    placement = 'above',
    tailAlign = 'end',
    anchor,
    className,
    style,
    children,
    onKeyDown,
    ...rest
}: BalloonProps) {
    const titleId = useId();
    const [element, setElement] = useState<HTMLDivElement | null>(null);
    const placed = useAnchoredPlace(anchor, element, placement);
    const side = placed?.side ?? placement;

    const anchoredStyle: CSSProperties | undefined = anchor
        ? ({
              position: 'fixed',
              left: placed?.x ?? 0,
              top: placed?.y ?? 0,
              visibility: placed ? undefined : 'hidden',
              '--ui-balloon-tip': `${placed?.tip ?? 0}px`,
          } as CSSProperties)
        : undefined;

    const balloon = (
        <div
            ref={setElement}
            role="dialog"
            aria-labelledby={title ? titleId : undefined}
            className={className ? `ui-balloon ${className}` : 'ui-balloon'}
            data-placement={side}
            data-tail-align={anchor ? 'anchor' : tailAlign}
            data-placed={!anchor || placed ? '' : undefined}
            style={{ ...anchoredStyle, ...style }}
            onKeyDown={(event) => {
                onKeyDown?.(event);
                if (
                    !event.defaultPrevented &&
                    event.key === 'Escape' &&
                    onClose
                ) {
                    event.stopPropagation();
                    onClose();
                }
            }}
            {...rest}
        >
            <div className="ui-balloon-body">
                {(title || icon || onClose) && (
                    <div className="ui-balloon-head">
                        {icon && (
                            <span
                                className="ui-balloon-icon"
                                aria-hidden="true"
                            >
                                {icon}
                            </span>
                        )}
                        <strong id={titleId} className="ui-balloon-title">
                            {title}
                        </strong>
                        {onClose && (
                            <button
                                type="button"
                                className="ui-balloon-close"
                                aria-label="Close"
                                title="Close"
                                onClick={onClose}
                            >
                                <Glyph name="close" />
                            </button>
                        )}
                    </div>
                )}
                <div className="ui-balloon-text">{children}</div>
            </div>
            <span className="ui-balloon-tail" aria-hidden="true" />
        </div>
    );

    return anchor ? (
        <ChromePortal owner={anchor}>{balloon}</ChromePortal>
    ) : (
        balloon
    );
}
