import type { PropsWithChildren } from "hono/jsx";

type WindowProps = {
    id?: string;
    title: string;
};
export function WindowContainer({
    children,
    id,
    title,
}: PropsWithChildren<WindowProps>) {
    return (
        <div
            id={id}
            className="
                window absolute w-[clamp(600px,80vw,1000px)] h-[clamp(400px,70vh,700px)]
                flex flex-col resize overflow-hidden min-w-[500px] min-h-[350px]"
            style="left: calc(50% - clamp(600px,80vw,1000px) / 2); top: calc(50% - clamp(400px,70vh,700px) / 2)"
        >
            <div
                className="title-bar box-content cursor-grab"
                x-on:pointerdown="
                    dragTarget = $event.target.closest('.window');
                    const rect = dragTarget.getBoundingClientRect();
                    dragTarget.offsetX = $event.clientX - rect.left;
                    dragTarget.offsetY = $event.clientY - rect.top;
                    $event.preventDefault(); "
            >
                <div className="title-bar-text"> {title} </div>
                <div className="title-bar-controls">
                    <button type="button" aria-label="Minimize"></button>
                    <button type="button" aria-label="Maximize"></button>
                    <button
                        type="button"
                        aria-label="Close"
                        x-on:click="$event.target.closest('.window').remove()"
                    ></button>
                </div>
            </div>
            <section className="flex-1 overflow-y-auto overflow-x-hidden">
                {children}
            </section>
        </div>
    );
}
