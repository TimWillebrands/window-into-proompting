import { type IconName, type IconSize, icons } from './registry.gen';

export type IconProps = {
    name: IconName;
    size?: IconSize;
    label?: string;
};

export function Icon({ name, size = 16, label }: IconProps) {
    const art = icons[name].sizes[size];
    return (
        <img
            className="ui-icon"
            src={art.src}
            width={size}
            height={size}
            alt={label ?? ''}
            aria-hidden={label ? undefined : true}
            data-tier={art.tier}
            draggable={false}
            decoding="async"
        />
    );
}
