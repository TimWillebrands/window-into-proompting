import {
    type ComponentPropsWithRef,
    type ReactNode,
    type SyntheticEvent,
    useId,
} from 'react';
import { Glyph, type GlyphName } from '../glyphs';

export type WindowProps = Omit<ComponentPropsWithRef<'section'>, 'title'> & {
    title: ReactNode;
    icon?: ReactNode;
    active?: boolean;
    maximised?: boolean;
    windowId?: string;
    onMinimise?: () => void;
    onMaximise?: () => void;
    onClose?: () => void;
    titleBarClassName?: string;
    titleBarAccessory?: ReactNode;
    menuBar?: ReactNode;
    statusBar?: ReactNode;
    bodyClassName?: string;
    skin?: string;
};

const keepFromDrag = (event: SyntheticEvent) => event.stopPropagation();

function Caption({
    glyph,
    label,
    kind,
    onPress,
}: {
    glyph: GlyphName;
    label: string;
    kind: 'minimise' | 'maximise' | 'close';
    onPress: () => void;
}) {
    return (
        <button
            type="button"
            className="ui-window-caption"
            data-caption={kind}
            aria-label={label}
            title={label}
            onPointerDown={keepFromDrag}
            onMouseDown={keepFromDrag}
            onTouchStart={keepFromDrag}
            onDoubleClick={keepFromDrag}
            onClick={(event) => {
                event.stopPropagation();
                onPress();
            }}
        >
            <Glyph name={glyph} />
        </button>
    );
}

const join = (...names: Array<string | false | undefined>) =>
    names.filter(Boolean).join(' ');

export function Window({
    title,
    icon,
    active = true,
    maximised = false,
    windowId,
    onMinimise,
    onMaximise,
    onClose,
    titleBarClassName,
    titleBarAccessory,
    menuBar,
    statusBar,
    bodyClassName,
    skin,
    className,
    children,
    ...rest
}: WindowProps) {
    const titleId = useId();
    return (
        <section
            aria-labelledby={titleId}
            className={join('ui-window', className)}
            data-inactive={active ? undefined : ''}
            data-maximised={maximised ? '' : undefined}
            data-ui-window-id={windowId}
            data-ui-skin={skin}
            {...rest}
        >
            {/* biome-ignore lint/a11y/noStaticElementInteractions: the title bar is the drag handle; double-click maximises, the caption buttons are the accessible route */}
            <header
                className={join('ui-window-titlebar', titleBarClassName)}
                onDoubleClick={onMaximise}
            >
                {icon && (
                    <span className="ui-window-icon" aria-hidden="true">
                        {icon}
                    </span>
                )}
                <span id={titleId} className="ui-window-title">
                    {title}
                </span>
                {titleBarAccessory && (
                    // biome-ignore lint/a11y/noStaticElementInteractions: keeps pointer presses on the accessory from starting a drag
                    <span
                        className="ui-window-accessory"
                        onPointerDown={keepFromDrag}
                        onMouseDown={keepFromDrag}
                        onTouchStart={keepFromDrag}
                        onDoubleClick={keepFromDrag}
                    >
                        {titleBarAccessory}
                    </span>
                )}
                <span className="ui-window-captions">
                    {onMinimise && (
                        <Caption
                            kind="minimise"
                            glyph="minimise"
                            label="Minimise"
                            onPress={onMinimise}
                        />
                    )}
                    {onMaximise && (
                        <Caption
                            kind="maximise"
                            glyph={maximised ? 'restore' : 'maximise'}
                            label={maximised ? 'Restore' : 'Maximise'}
                            onPress={onMaximise}
                        />
                    )}
                    {onClose && (
                        <Caption
                            kind="close"
                            glyph="close"
                            label="Close"
                            onPress={onClose}
                        />
                    )}
                </span>
            </header>
            {menuBar && <div className="ui-window-menubar">{menuBar}</div>}
            <div className={join('ui-window-body', bodyClassName)}>
                {children}
            </div>
            {statusBar && (
                <footer className="ui-window-statusbar">{statusBar}</footer>
            )}
        </section>
    );
}
