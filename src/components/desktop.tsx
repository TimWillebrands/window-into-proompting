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
                windows: $persist({ }),
                hasSeenWelcome: $persist(false)
            }"
            x-init="
                if (!hasSeenWelcome) {
                    htmx.ajax('GET', '/welcome', {target: '#windows', swap: 'beforeend'});
                    hasSeenWelcome = true;
                }
            "
            x-on:pointermove="if(dragTarget && dragTarget.offsetX !== undefined) {
                const newX = $event.clientX - dragTarget.offsetX;
                const newY = $event.clientY - dragTarget.offsetY;
                dragTarget.style.left = newX + 'px';
                dragTarget.style.top = newY + 'px';
                dragTarget.style.zIndex = '1000';
            }"
            className="fixed inset-0 w-screen h-screen flex justify-center items-center bg-gradient-to-br from-slate-300 via-slate-400 to-slate-300"
        >
            <section id="appIcons" class="w-full h-full">
                <Icon
                    icon="842"
                    label="Welcome"
                    x={0}
                    y={0}
                    hx-get="/welcome"
                    hx-target="#windows"
                    hx-swap="beforeend"
                    // Only open if not already open
                    hx-trigger="click[!window.document.getElementById('welcome')]"
                />
                <Icon
                    icon="1012"
                    label="Personas"
                    x={0}
                    y={100}
                    hx-get="/personas"
                    hx-target="#windows"
                    hx-swap="beforeend"
                    // Only open if not already open
                    hx-trigger="click[!window.document.getElementById('personas')]"
                />
                <Icon
                    icon="1012"
                    label="Open Chat"
                    x={0}
                    y={200}
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
