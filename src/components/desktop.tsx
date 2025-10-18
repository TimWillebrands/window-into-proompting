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
                hasSeenWelcome: $persist(false),
                focusedApp: null
            }"
            x-init="
                // Check URL for app parameter and auto-open window
                const urlParams = new URLSearchParams(window.location.search);
                const appParam = urlParams.get('app');
                if (appParam) {
                    if (appParam.startsWith('party-')) {
                        const partyId = appParam.replace('party-', '');
                        htmx.ajax('GET', `/party/${partyId}`, {target: '#windows', swap: 'beforeend'});
                        focusedApp = partyId; // Use the actual room ID, not the URL format
                    } else if (['welcome', 'personas', 'open-party'].includes(appParam)) {
                        htmx.ajax('GET', `/${appParam}`, {target: '#windows', swap: 'beforeend'});
                        focusedApp = appParam;
                    }
                } else if (!hasSeenWelcome) {
                    htmx.ajax('GET', '/welcome', {target: '#windows', swap: 'beforeend'});
                    hasSeenWelcome = true;
                    focusedApp = 'welcome';
                }
            "
            x-effect="
                // Update URL when focusedApp changes
                if (focusedApp) {
                    const url = new URL(window.location);
                    // For party windows (UUID format), use party-{id} format in URL
                    const isPartyId = focusedApp.match(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
                    const appParam = isPartyId ? `party-${focusedApp}` : focusedApp;
                    url.searchParams.set('app', appParam);
                    window.history.replaceState({}, '', url);
                } else {
                    const url = new URL(window.location);
                    url.searchParams.delete('app');
                    window.history.replaceState({}, '', url);
                }
            "
            x-on:pointermove="if(dragTarget && dragTarget.offsetX !== undefined) {
                // For icons, check if we've moved enough to start dragging
                if(dragTarget.startX !== undefined && !dragTarget.isDragging) {
                    const deltaX = Math.abs($event.clientX - dragTarget.startX);
                    const deltaY = Math.abs($event.clientY - dragTarget.startY);
                    if(deltaX > 5 || deltaY > 5) {
                        dragTarget.isDragging = true;
                    }
                }
                // Only move if we're dragging or if it's a window (which always drags)
                if(!dragTarget.startX || dragTarget.isDragging) {
                    const newX = $event.clientX - dragTarget.offsetX;
                    const newY = $event.clientY - dragTarget.offsetY;
                    dragTarget.style.left = newX + 'px';
                    dragTarget.style.top = newY + 'px';
                    dragTarget.style.zIndex = '1000';
                }
            }"
            x-on:pointerup="if(dragTarget && dragTarget.classList && dragTarget.classList.contains('window')) {
                // Window drag handling (existing behavior)
                const w = windows[dragTarget.id];
                if(w){
                    w.x = dragTarget.style.left;
                    w.y = dragTarget.style.top;
                }
                dragTarget = null;
            } else if(dragTarget && dragTarget.offsetX !== undefined) {
                // Icon drag handling - snap to grid if we actually dragged
                if(dragTarget.isDragging) {
                    const grid = 100;
                    const left = parseInt(dragTarget.style.left || '0', 10);
                    const top = parseInt(dragTarget.style.top || '0', 10);
                    const snappedLeft = Math.round(left / grid) * grid;
                    const snappedTop = Math.round(top / grid) * grid;
                    dragTarget.style.left = snappedLeft + 'px';
                    dragTarget.style.top = snappedTop + 'px';
                    dragTarget.style.zIndex = '';
                    // Set flag to prevent click from opening window
                    const alpineData = Alpine.$data(dragTarget);
                    if(alpineData) alpineData.wasDragged = true;
                }
                delete dragTarget.offsetX;
                delete dragTarget.offsetY;
                delete dragTarget.startX;
                delete dragTarget.startY;
                delete dragTarget.isDragging;
                dragTarget = null;
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
