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
            x-data="{ selected: false }"
            class="absolute m-1 w-24 h-24 cursor-default select-none
                outline-none"
            style={{ top: y, left: x, background: "none" }}
            x-on:pointerdown="
                $event.preventDefault();
                $event.stopPropagation();
                selected = true;
                const btn = $event.currentTarget;
                const rect = btn.getBoundingClientRect();
                dragTarget = btn;
                dragTarget.offsetX = $event.clientX - rect.left;
                dragTarget.offsetY = $event.clientY - rect.top;
            "
            x-on:keydown="if ($event.key === 'Enter') { $event.preventDefault(); $event.stopPropagation(); $event.currentTarget.click() }"
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
