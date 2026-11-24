import {
    Children,
    type ComponentPropsWithRef,
    type CSSProperties,
    isValidElement,
    type ReactNode,
    type Ref,
} from 'react';

export type WindowLayoutBreakpoint = 520 | 640 | 820 | 1000;

export type PaneWidth = 'regular' | 'wide' | (string & {});

export type WindowLayoutProps = Omit<ComponentPropsWithRef<'div'>, 'title'> & {
    menuBar?: ReactNode;
    toolbar?: ReactNode;
    addressBar?: ReactNode;
    taskPane?: ReactNode;
    taskPaneLabel?: string;
    taskPaneWidth?: PaneWidth;
    taskPaneNarrow?: 'stack' | 'hide';
    narrowBelow?: WindowLayoutBreakpoint;
    detailsPane?: ReactNode;
    detailsLabel?: string;
    detailsPlacement?: 'end' | 'bottom';
    detailsWidth?: PaneWidth;
    detailsNarrow?: 'bottom' | 'hide';
    detailsNarrowBelow?: WindowLayoutBreakpoint;
    footer?: ReactNode;
    statusBar?: ReactNode;
    scroll?: 'content' | 'both' | 'none';
    contentLabel?: string;
    contentRef?: Ref<HTMLElement>;
    contentClassName?: string;
};

const named = (width: PaneWidth) => width === 'regular' || width === 'wide';

const paneWidth = (width: PaneWidth): CSSProperties | undefined =>
    named(width)
        ? undefined
        : ({ '--ui-window-layout-pane-width': width } as CSSProperties);

export function WindowLayout({
    menuBar,
    toolbar,
    addressBar,
    taskPane,
    taskPaneLabel = 'Tasks',
    taskPaneWidth = 'regular',
    taskPaneNarrow = 'stack',
    narrowBelow = 520,
    detailsPane,
    detailsLabel = 'Details',
    detailsPlacement = 'end',
    detailsWidth = 'regular',
    detailsNarrow = 'bottom',
    detailsNarrowBelow = narrowBelow,
    footer,
    statusBar,
    scroll = 'content',
    contentLabel,
    contentRef,
    contentClassName,
    className,
    children,
    ...rest
}: WindowLayoutProps) {
    const bands = Children.toArray(toolbar);
    const scrolls = scroll !== 'none';
    return (
        <div
            className={
                className ? `ui-window-layout ${className}` : 'ui-window-layout'
            }
            {...rest}
        >
            {menuBar && (
                <div className="ui-window-layout-menubar">{menuBar}</div>
            )}
            {bands.map((band, index) => (
                <div
                    key={isValidElement(band) ? (band.key ?? index) : index}
                    className="ui-window-layout-band"
                    data-band="toolbar"
                >
                    {band}
                </div>
            ))}
            {addressBar && (
                <div className="ui-window-layout-band" data-band="address">
                    {addressBar}
                </div>
            )}
            <div
                className="ui-window-layout-panes"
                data-narrow-below={narrowBelow}
            >
                {taskPane && (
                    <nav
                        className="ui-window-layout-taskpane"
                        aria-label={taskPaneLabel}
                        data-width={
                            named(taskPaneWidth) ? taskPaneWidth : 'custom'
                        }
                        data-narrow={taskPaneNarrow}
                        style={paneWidth(taskPaneWidth)}
                    >
                        {taskPane}
                    </nav>
                )}
                <div
                    className="ui-window-layout-main"
                    data-details={detailsPane ? detailsPlacement : undefined}
                    data-narrow-below={detailsNarrowBelow}
                >
                    <section
                        ref={contentRef}
                        className={
                            contentClassName
                                ? `ui-window-layout-content ${contentClassName}`
                                : 'ui-window-layout-content'
                        }
                        data-scroll={scroll}
                        aria-label={contentLabel}
                        tabIndex={scrolls ? 0 : undefined}
                    >
                        {children}
                    </section>
                    {detailsPane && (
                        <aside
                            className="ui-window-layout-details"
                            aria-label={detailsLabel}
                            data-width={
                                named(detailsWidth) ? detailsWidth : 'custom'
                            }
                            data-narrow={detailsNarrow}
                            style={paneWidth(detailsWidth)}
                            // biome-ignore lint/a11y/noNoninteractiveTabindex: a scrolling region must be reachable by keyboard
                            tabIndex={0}
                        >
                            {detailsPane}
                        </aside>
                    )}
                </div>
            </div>
            {footer && <div className="ui-window-layout-footer">{footer}</div>}
            {statusBar && (
                <div className="ui-window-layout-statusbar">{statusBar}</div>
            )}
        </div>
    );
}
