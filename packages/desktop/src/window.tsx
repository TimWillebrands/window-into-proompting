import type { PropsWithChildren } from "hono/jsx";

type WindowProps = {
    id?: string;
    title: string;
    url?: string;
    icon?: string;
};
export function WindowContainer({
    children,
    id,
    title,
    url,
    icon,
}: PropsWithChildren<WindowProps>) {
    return (
        <div
            id={id}
            x-data={`{ url: '${url}', title: '${title}', windowId: '${id}'}`}
            x-on:click="(() => {
                // Set this window as focused when clicked and bring to front
                const desktopData = Alpine.$data(document.querySelector('main'));
                if (desktopData) {
                    desktopData.focusedApp = windowId;
                    const z = ++desktopData.zCounter;
                    $el.style.zIndex = z;
                }
            })()"
            className="
                window absolute w-[clamp(600px,80vw,1000px)] h-[clamp(400px,70vh,700px)]
                flex flex-col resize overflow-hidden min-w-[500px] min-h-[350px]
                shadow-[4px_4px_8px_rgba(0,0,0,0.3)]"
            x-init="(() => {
                const windowData = windows[windowId];
                $el.style.left = windowData?.x ?? 'calc(50% - clamp(600px,80vw,1000px) / 2)';
                $el.style.top = windowData?.y ?? 'calc(50% - clamp(400px,70vh,700px) / 2)';
                $el.style.width = windowData?.width ?? null;
                $el.style.height = windowData?.height ?? null;
                if(windowId && url){
                    windows[windowId] = {
                        id: windowId,
                        title: title,
                        url: url,
                        x: $el.style.left,
                        y: $el.style.top,
                        width: $el.style.width,
                        height: $el.style.height
                    };
                }
                // Set this window as focused when it's created
                const desktopData = Alpine.$data(document.querySelector('main'));
                if (desktopData) {
                    desktopData.focusedApp = windowId;
                }
            })()"
            x-resize="(() => {
                const windowData = windows[windowId];
                if(windowData){
                    windowData.width = $el.style.width;
                    windowData.height = $el.style.height;
                }
            })()"
        >
            <div
                className="title-bar box-content cursor-grab active"
                x-on:pointerdown="
                    dragTarget = $event.target.closest('.window');
                    const rect = dragTarget.getBoundingClientRect();
                    dragTarget.offsetX = $event.clientX - rect.left;
                    dragTarget.offsetY = $event.clientY - rect.top;
                    $event.preventDefault();
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
                        x-on:click="
                            windows[windowId] = undefined;
                            const desktopData = Alpine.$data(document.querySelector('main'));
                            if (desktopData && desktopData.focusedApp === windowId) {
                                desktopData.focusedApp = null;
                            }
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
