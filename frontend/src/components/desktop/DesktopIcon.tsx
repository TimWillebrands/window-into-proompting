import { DesktopIcon as DesktopIconPart } from '@partytown/ui';
import AppIcon from './AppIcon';

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
        <DesktopIconPart
            className="absolute"
            style={{ left: position.x, top: position.y }}
            icon={<AppIcon icon={icon} size={48} />}
            label={label}
            onClick={() => onActivate()}
        />
    );
}
