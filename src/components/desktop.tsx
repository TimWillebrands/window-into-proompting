import type { PropsWithChildren } from "hono/jsx";
import { Taskbar } from "./taskbar";

/**
 * Container for all parties, which manages the layout and behavior of party windows.
 */
export function Desktop({ children }: PropsWithChildren<unknown>) {
    return (
        <main
            x-data="{
                dragTarget: null,
                windows: $persist({ })
            }"
            x-on:pointermove="if(dragTarget && dragTarget.offsetX !== undefined) {
                const newX = event.clientX - dragTarget.offsetX;
                const newY = event.clientY - dragTarget.offsetY;
                dragTarget.style.left = newX + 'px';
                dragTarget.style.top = newY + 'px';
                dragTarget.style.zIndex = '1000';
            }"
            className="fixed inset-0 w-screen h-screen flex justify-center items-center bg-gradient-to-br from-slate-300 via-slate-400 to-slate-300"
        >
            <section id="appIcons" class="w-full h-full">
                <Icon icon="1012" label="Personas" x={0} y={0} />
                <Icon
                    icon="842"
                    label="Open Chat"
                    x={0}
                    y={84}
                    hx-get="/party"
                    hx-target="#windows"
                    hx-swap="beforeend"
                    // Only open if not already open
                    hx-trigger="click[!window.document.getElementById('open-party')]"
                />
            </section>

            <section id="windows" class="w-full h-full pb-10">
                <template x-for="(windowData, index) in windows">
                    <div
                        hx-trigger="load"
                        x-bind:hx-get="windowData.url"
                        x-text="windowData.title"
                        hx-swap="outerHTML"
                    />
                </template>
                {children}
            </section>

            <Taskbar />
        </main>
    );
}

function Icon({
    icon,
    label,
    x,
    y,
    ...hxAttributes
}: {
    icon: string;
    label: string;
    x: number;
    y: number;
    [hxAttr: string]: unknown;
}) {
    return (
        <button
            type="button"
            id={label}
            class="m-1 absolute h-20 w-20"
            style={{ top: y, left: x }}
            draggable
            x-on:dragend="
                const btn = $event.target.closest('button')
                btn.style.left = $event.clientX + 'px';
                btn.style.top = $event.clientY + 'px';"
            {...hxAttributes}
        >
            <img src={`img/${icon}.ico`} alt={`Icon to open ${label} app`} />
            {label}
        </button>
    );
}
