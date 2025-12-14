import type { PropsWithChildren } from "hono/jsx";
import { Window } from "hono/jsx/dom";
import { Icon } from "./icon";
import { Taskbar } from "./taskbar";
import { WindowContainer } from "./window";

/**
 * Container for all parties, which manages the layout and behavior of party windows.
 */
export function Desktop({ children }: PropsWithChildren<unknown>) {
    return (
        <main
            x-data="{ dragTarget: null }"
            x-init="$store.desktop.init()"
            x-on:pointermove="if(dragTarget) {
                const newX = $event.clientX - dragTarget.offsetX;
                const newY = $event.clientY - dragTarget.offsetY;
                dragTarget.style.left = newX + 'px';
                dragTarget.style.top = newY + 'px';
            }"
            x-on:pointerup="if(dragTarget) {
                const w = $store.desktop.windows[dragTarget.id];
                if(w){
                    w.x = dragTarget.style.left;
                    w.y = dragTarget.style.top;
                }
                dragTarget = null;
            }"
            className="fixed inset-0 w-screen h-screen flex justify-center items-center bg-gradient-to-br from-slate-300 via-slate-400 to-slate-300 bg-[url('/img/blissful_penger.webp')] bg-cover bg-center bg-no-repeat"
        >
            <section id="appIcons" class="w-full h-full">
                <Icon
                    icon="842"
                    label="Welcome"
                    x={0}
                    y={0}
                    x-on:click="$store.desktop.openWindow({id: 'welcome', title: 'Welcome', url: '/welcome'})"
                />
                <Icon
                    icon="1012"
                    label="Personas"
                    x={0}
                    y={100}
                    x-on:click="$store.desktop.openWindow({id: 'personas', title: 'Personas', url: '/personas'})"
                />
                <Icon
                    icon="1012"
                    label="Open Chat"
                    x={0}
                    y={200}
                    x-on:click="$store.desktop.openWindow({id: 'open-party', title: 'Open Chat', url: '/party'})"
                />
            </section>

            <section id="windows" class="w-full h-full pb-8">
                <template x-for="w in Object.values($store.desktop.windows)" :key="w.id">
                    <WindowContainer
                        id={w.id}
                        title={w.title}
                        style={`top: ${w.y}; left: ${w.x}; z-index: ${w.z}`}
                        x-show="w.id"
                        hx-trigger="load"
                        hx-get={w.url}
                        hx-swap="innerHTML"
                        x-on:pointerdown.self="$store.desktop.focusWindow(w.id)"
                    >
                        <div class="window-body">
                            <p>Loading...</p>
                        </div>
                    </WindowContainer>
                </template>
                {children}
            </section>

            <Taskbar />
        </main>
    );
}
