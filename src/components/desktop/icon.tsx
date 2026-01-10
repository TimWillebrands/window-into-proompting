export function Icon({
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
            x-data="{ selected: false, wasDragged: false }"
            class="absolute m-1 w-24 h-24 cursor-default select-none
                outline-none"
            style={{ top: y, left: x, background: "none" }}
            x-on:pointerdown="
                $event.stopPropagation();
                selected = true;
                wasDragged = false;
                dragTarget = $el;
                const rect = $el.getBoundingClientRect();
                dragTarget.offsetX = $event.clientX - rect.left;
                dragTarget.offsetY = $event.clientY - rect.top;
                dragTarget.startX = $event.clientX;
                dragTarget.startY = $event.clientY;
                dragTarget.isDragging = false;
            "
            x-on:click="if(wasDragged) { $event.preventDefault(); $event.stopPropagation(); wasDragged = false; }"
            tabIndex={0}
            {...hxAttributes}
        >
            <div class="flex flex-col items-center">
                <img
                    class="h-12 w-12 drop-shadow-md"
                    src={`img/${icon}.ico`}
                    alt={`Icon to open ${label} app`}
                    draggable={false}
                />
                <div class="mt-1 text-center leading-tight text-white text-sm">
                    <span
                        x-bind:class="selected ? 'bg-blue-600/80 px-1 rounded-[2px]' : ''"
                        class="[text-shadow:1px_1px_1px_rgba(0,0,0)]"
                    >
                        {label}
                    </span>
                </div>
            </div>
        </button>
    );
}
