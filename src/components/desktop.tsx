import type { PropsWithChildren } from "hono/jsx";
import { Icon } from "./icon";
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
                const newX = $event.clientX - dragTarget.offsetX;
                const newY = $event.clientY - dragTarget.offsetY;
                dragTarget.style.left = newX + 'px';
                dragTarget.style.top = newY + 'px';
                dragTarget.style.zIndex = '1000';
            }"
            x-on:pointerup="if(dragTarget) {
                const grid = 100;
                const left = parseInt(dragTarget.style.left || '0', 10);
                const top = parseInt(dragTarget.style.top || '0', 10);
                const snappedLeft = Math.round(left / grid) * grid;
                const snappedTop = Math.round(top / grid) * grid;
                dragTarget.style.left = snappedLeft + 'px';
                dragTarget.style.top = snappedTop + 'px';
                dragTarget.style.zIndex = '';
                dragTarget = null;
            }"
            x-on:mouseleave="if(dragTarget) { dragTarget.style.zIndex = ''; dragTarget = null }"
            className="fixed inset-0 w-screen h-screen flex justify-center items-center bg-gradient-to-br from-slate-300 via-slate-400 to-slate-300"
        >
            <section id="appIcons" class="w-full h-full">
                <Icon icon="1012" label="Personas" x={0} y={0} />
                <Icon
                    icon="842"
                    label="Open Chat"
                    x={0}
                    y={100}
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
