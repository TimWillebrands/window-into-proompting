import {
    CommandButton,
    GroupBox,
    Icon,
    type IconName,
    Presence,
    Text,
} from '@partytown/ui';

interface RoomCardProps {
    name: string;
    description: string;
    icon: IconName;
    online: number;
    members: number;
    onClick?: () => void;
    disabled?: boolean;
    size?: 'lg' | 'md';
}

const fmt = (n: number) =>
    n >= 1000 ? `${(n / 1000).toFixed(n >= 10000 ? 0 : 1)}k` : `${n}`;

export default function RoomCard({
    name,
    description,
    icon,
    online,
    members,
    onClick,
    disabled,
    size = 'lg',
}: RoomCardProps) {
    const isLg = size === 'lg';
    return (
        <GroupBox
            title={name}
            icon={<Icon name={icon} />}
            className={`content-between ${isLg ? 'min-h-[144px]' : 'min-h-[112px]'}`}
        >
            <div className="flex items-start gap-3">
                <Icon name={icon} size={isLg ? 48 : 32} />
                <Text
                    as="p"
                    size={isLg ? 'reading' : undefined}
                    className={isLg ? undefined : 'line-clamp-2'}
                >
                    {description}
                </Text>
            </div>
            <div className="flex flex-wrap items-center gap-3">
                <Text tone="muted" className="flex items-center gap-1">
                    <Presence status="online" size="small" />
                    {fmt(online)} Online
                </Text>
                <Text tone="muted" className="flex items-center gap-1">
                    <Icon name="users" />
                    {fmt(members)} Members
                </Text>
                <CommandButton
                    size="small"
                    className="ml-auto"
                    onClick={onClick}
                    disabled={disabled}
                    aria-label={`Enter ${name}`}
                    title={
                        disabled
                            ? 'Example room — joining is not available yet'
                            : undefined
                    }
                >
                    Enter
                </CommandButton>
            </div>
        </GroupBox>
    );
}
