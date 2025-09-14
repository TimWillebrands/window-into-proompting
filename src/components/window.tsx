import type { PropsWithChildren } from "hono/jsx";

type WindowProps = {
    id?: string;
    title: string;
    url?: string;
};
export function WindowContainer({
    children,
    id,
    title,
    url,
}: PropsWithChildren<WindowProps>) {
    return (
        <div
            id={id}
            x-data={`{ url: '${url}', title: '${title}', id: '${id}'}`}
            className="
                window absolute w-[clamp(600px,80vw,1000px)] h-[clamp(400px,70vh,700px)]
                flex flex-col resize overflow-hidden min-w-[500px] min-h-[350px]"
            x-init="
                const windowData = windows[id];
                $el.style.left = windowData?.x ?? 'calc(50% - clamp(600px,80vw,1000px) / 2)';
                $el.style.top = windowData?.y ?? 'calc(50% - clamp(400px,70vh,700px) / 2)';
                $el.style.width = windowData?.width ?? null;
                $el.style.height = windowData?.height ?? null;
                if($el.id && url){
                    windows[$el.id] = {
                        id: $el.id,
                        title: title,
                        url: url,
                        x: $el.style.left,
                        y: $el.style.top,
                        width: $el.style.width,
                        height: $el.style.height
                    };
                }
            "
        >
            <div
                className="title-bar box-content cursor-grab"
                x-on:pointerdown="
                    dragTarget = $event.target.closest('.window');
                    const rect = dragTarget.getBoundingClientRect();
                    dragTarget.offsetX = $event.clientX - rect.left;
                    dragTarget.offsetY = $event.clientY - rect.top;
                    $event.preventDefault();
                    document.addEventListener('pointerup', () => {
                        const w = windows[$root.id]
                        if(w){
                            w.x = dragTarget.style.left;
                            w.y = dragTarget.style.top;
                        }
                        console.log('Pointer up event triggered', w, windows);
                        dragTarget = null;
                    }, {once: true});
                "
            >
                <div className="title-bar-text"> {title} </div>
                <div className="title-bar-controls">
                    <button type="button" aria-label="Minimize"></button>
                    <button type="button" aria-label="Maximize"></button>
                    <button
                        type="button"
                        aria-label="Close"
                        x-on:click="
                            windows[$root.id] = undefined;
                            $event.target.closest('.window').remove(); "
                    ></button>
                </div>
            </div>
            <section className="flex-1 overflow-y-auto overflow-x-hidden flex flex-col">
                {children}
            </section>
        </div>
    );
}
