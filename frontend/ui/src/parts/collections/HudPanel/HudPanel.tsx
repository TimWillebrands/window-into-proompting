import { type ComponentPropsWithRef, type ReactNode, useId } from 'react';
import { Glyph } from '../../chrome/glyphs';
import { ToolbarVariantContext } from '../../controls/Toolbar/Toolbar';

export type HudPanelAnchor =
    | 'top-start'
    | 'top'
    | 'top-end'
    | 'bottom-start'
    | 'bottom'
    | 'bottom-end';

export type HudPanelProps = Omit<ComponentPropsWithRef<'div'>, 'title'> & {
    title?: ReactNode;
    icon?: ReactNode;
    anchor?: HudPanelAnchor;
    onClose?: () => void;
    closeLabel?: string;
};

export function HudPanel({
    title,
    icon,
    anchor,
    onClose,
    closeLabel = 'Close',
    className,
    children,
    onKeyDown,
    ...rest
}: HudPanelProps) {
    const titleId = useId();
    const hasHead = Boolean(title || icon || onClose);
    return (
        // biome-ignore lint/a11y/useSemanticElements: a floating panel over a canvas, not a form fieldset
        <div
            role="group"
            aria-labelledby={title ? titleId : undefined}
            className={className ? `ui-hud-panel ${className}` : 'ui-hud-panel'}
            data-anchor={anchor}
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
            {hasHead && (
                <div className="ui-hud-panel-head">
                    {icon && (
                        <span className="ui-hud-panel-icon" aria-hidden="true">
                            {icon}
                        </span>
                    )}
                    {title && (
                        <strong id={titleId} className="ui-hud-panel-title">
                            {title}
                        </strong>
                    )}
                    {onClose && (
                        <button
                            type="button"
                            className="ui-hud-panel-close"
                            aria-label={closeLabel}
                            title={closeLabel}
                            onClick={onClose}
                        >
                            <Glyph name="close" />
                        </button>
                    )}
                </div>
            )}
            {children !== undefined && children !== null && (
                <div className="ui-hud-panel-body">
                    <ToolbarVariantContext value="bare">
                        {children}
                    </ToolbarVariantContext>
                </div>
            )}
        </div>
    );
}
