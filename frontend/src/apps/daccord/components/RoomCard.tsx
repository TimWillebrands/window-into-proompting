import {
    CommandButton,
    GroupBox,
    Icon,
    type IconName,
    Presence,
    Stack,
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
            <Stack direction="row" align="start" gap="lg">
                <Icon name={icon} size={isLg ? 48 : 32} />
                <Text
                    as="p"
                    size={isLg ? 'reading' : undefined}
                    className={isLg ? undefined : 'line-clamp-2'}
                >
                    {description}
                </Text>
            </Stack>
            <Stack direction="row" wrap align="center" gap="lg">
                <Stack direction="row" align="center" gap="xs">
                    <Presence status="online" size="small" />
                    <Text tone="muted">{fmt(online)} Online</Text>
                </Stack>
                <Stack direction="row" align="center" gap="xs">
                    <Icon name="users" />
                    <Text tone="muted">{fmt(members)} Members</Text>
                </Stack>
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
            </Stack>
        </GroupBox>
    );
}
