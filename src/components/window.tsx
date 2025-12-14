import type { PropsWithChildren } from "hono/jsx";

type WindowProps = {
    id?: string;
    title: string;
    icon?: string;
};
export function WindowContainer({
    children,
    id,
    title,
    icon,
    ...rest
}: PropsWithChildren<WindowProps>) {
    return (
        <div
            id={id}
            className="window absolute w-[clamp(600px,80vw,1000px)] h-[clamp(400px,70vh,700px)]
                flex flex-col resize overflow-hidden min-w-[500px] min-h-[350px]
                shadow-[4px_4px_8px_rgba(0,0,0,0.3)]"
            {...rest}
        >
            <div
                className="title-bar box-content cursor-grab active"
                x-on:pointerdown="
                    dragTarget = $el.closest('.window');
                    const rect = dragTarget.getBoundingClientRect();
                    dragTarget.offsetX = $event.clientX - rect.left;
                    dragTarget.offsetY = $event.clientY - rect.top;
                "
            >
                <div className="title-bar-text">
                    {icon && <span className="mr-1">{icon}</span>}
                    {title}
                </div>
                <div className="title-bar-controls">
                    <button type="button" aria-label="Minimize"></button>
                    <button type="button" aria-label="Maximize"></button>
                    <button
                        type="button"
                        aria-label="Close"
                        x-on:click.stop={`$store.desktop.closeWindow('${id}')`}
                    ></button>
                </div>
            </div>
            <section className="flex-1 overflow-y-auto overflow-x-hidden flex flex-col">
                {children}
            </section>
        </div>
    );
}
