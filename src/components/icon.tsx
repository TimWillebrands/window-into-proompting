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
            x-data="{ selected: false, dragging: false }"
            class="absolute m-1 w-24 h-24 cursor-default select-none
                outline-none"
            style={{ top: y, left: x, background: "none" }}
            x-on:pointerdown="
                $event.preventDefault();
                $event.stopPropagation();
                selected = true;
                dragging = true;
                const rect = $el.getBoundingClientRect();
                $el.offsetX = $event.clientX - rect.left;
                $el.offsetY = $event.clientY - rect.top;
            "
            x-on:pointermove="if($el && $el.offsetX !== undefined && dragging) {
                const newX = $event.clientX - $el.offsetX;
                const newY = $event.clientY - $el.offsetY;
                $el.style.left = newX + 'px';
                $el.style.top = newY + 'px';
                $el.style.zIndex = '1000';
            }"
            x-on:pointerup="
                const grid = 100;
                console.log($el, $el.style)
                const left = parseInt($el.style.left || '0', 10);
                const top = parseInt($el.style.top || '0', 10);
                const snappedLeft = Math.round(left / grid) * grid;
                const snappedTop = Math.round(top / grid) * grid;
                $el.style.left = snappedLeft + 'px';
                $el.style.top = snappedTop + 'px';
                $el.style.zIndex = '';
                $el = null;
                dragging = false;
            "
            x-on:keydown="if ($event.key === 'Enter') {
                $event.preventDefault();
                $event.stopPropagation();
                $event.currentTarget.click()
            }"
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
