import {
    type ComponentPropsWithRef,
    createContext,
    type Dispatch,
    type ReactNode,
    type SetStateAction,
    type SyntheticEvent,
    useContext,
    useEffect,
    useId,
    useState,
} from 'react';
import { type SkinName, skinOf } from '../../../../skins';
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
    bodyClassName?: string;
    skin?: SkinName;
};

const AccessoryContext = createContext<Dispatch<
    SetStateAction<ReactNode>
> | null>(null);

export function useTitleBarAccessory(node: ReactNode) {
    const set = useContext(AccessoryContext);
    useEffect(() => {
        if (!set) return;
        set(node);
        return () => set(null);
    }, [set, node]);
}

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
    bodyClassName,
    skin,
    className,
    children,
    ...rest
}: WindowProps) {
    const titleId = useId();
    const [placed, setPlaced] = useState<ReactNode>(null);
    const accessory = titleBarAccessory ?? placed;
    const look = skinOf(skin);
    const maximise = look?.maximise === false ? undefined : onMaximise;
    const regions = (place: 'frame' | 'titlebar') =>
        look?.regions
            ?.filter((region) => region.in === place)
            .map((region) => (
                <span
                    key={region.name}
                    className="ui-window-skin-region"
                    data-region={region.name}
                    aria-hidden="true"
                />
            ));
    return (
        <section
            aria-labelledby={titleId}
            className={join('ui-window', className)}
            data-inactive={active ? undefined : ''}
            data-maximised={maximised ? '' : undefined}
            data-ui-window-id={windowId}
            data-ui-skin={skin}
            data-title-bar={look?.titleBar === 'tall' ? 'tall' : undefined}
            {...rest}
        >
            {regions('frame')}
            {/* biome-ignore lint/a11y/noStaticElementInteractions: the title bar is the drag handle; double-click maximises, the caption buttons are the accessible route */}
            <header
                className={join('ui-window-titlebar', titleBarClassName)}
                onDoubleClick={maximise}
            >
                {regions('titlebar')}
                {icon && (
                    <span className="ui-window-icon" aria-hidden="true">
                        {icon}
                    </span>
                )}
                <span id={titleId} className="ui-window-title">
                    {title}
                </span>
                {accessory && (
                    // biome-ignore lint/a11y/noStaticElementInteractions: keeps pointer presses on the accessory from starting a drag
                    <span
                        className="ui-window-accessory"
                        onPointerDown={keepFromDrag}
                        onMouseDown={keepFromDrag}
                        onTouchStart={keepFromDrag}
                        onDoubleClick={keepFromDrag}
                    >
                        {accessory}
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
                    {maximise && (
                        <Caption
                            kind="maximise"
                            glyph={maximised ? 'restore' : 'maximise'}
                            label={maximised ? 'Restore' : 'Maximise'}
                            onPress={maximise}
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
            <div className={join('ui-window-body', bodyClassName)}>
                <AccessoryContext.Provider value={setPlaced}>
                    {children}
                </AccessoryContext.Provider>
            </div>
        </section>
    );
}
