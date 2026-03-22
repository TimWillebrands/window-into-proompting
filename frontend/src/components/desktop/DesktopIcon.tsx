interface DesktopIconProps {
    icon: string;
    label: string;
    position: { x: number; y: number };
    onActivate: () => void;
}

export default function DesktopIcon({
    icon,
    label,
    position,
    onActivate,
}: DesktopIconProps) {
    return (
        <button
            type="button"
            className="absolute w-24 h-24 flex flex-col items-center justify-center text-white"
            style={{
                background: 'none',
                left: position.x,
                top: position.y,
            }}
            onClick={() => onActivate()}
        >
            <img src={icon} alt={label} className="h-12 w-12 drop-shadow-md" />
            <span className="mt-1 text-sm text-center bg-blue-600/70 rounded px-1">
                {label}
            </span>
        </button>
    );
}
