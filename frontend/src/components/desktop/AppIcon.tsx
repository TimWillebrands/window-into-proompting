import { Icon, type IconName, type IconSize, iconNames } from '@partytown/ui';

const isImagePath = (icon: string) =>
    /\.(png|jpe?g|webp|svg|ico|gif)$/i.test(icon) || icon.startsWith('/');

const isIconName = (icon: string): icon is IconName =>
    iconNames.some((name) => name === icon);

const kitName = (icon: string): IconName | undefined => {
    const base = icon.match(/([^/]+)\.[a-z]+$/i)?.[1];
    return iconNames.find((name) => name === base);
};

export default function AppIcon({
    icon,
    size = 16,
}: {
    icon: string | undefined;
    size?: IconSize;
}) {
    if (!icon) return <Icon name="document" size={size} />;
    if (isIconName(icon)) return <Icon name={icon} size={size} />;
    if (!isImagePath(icon)) return <span>{icon}</span>;
    const name = kitName(icon);
    if (name) return <Icon name={name} size={size} />;
    return <img src={icon} alt="" width={size} height={size} />;
}
