import {
    FieldRow,
    GroupBox,
    Hyperlink,
    Icon,
    type IconName,
    PageHeader,
    Pane,
    Stack,
    Text,
    type Tone,
} from '@partytown/ui';
import type { ReactNode } from 'react';
import type { ProblemDetails } from '../../api/model';
import { refusal, signedPercent, toneOf } from './jobs-lib';

export function PageHead({
    icon,
    title,
    sub,
    chip,
}: {
    icon: IconName;
    title: string;
    sub?: string;
    chip?: string;
}) {
    return (
        <PageHeader
            icon={<Icon name={icon} size={32} />}
            title={title}
            description={sub || undefined}
            aside={chip || undefined}
        />
    );
}

export function Page({ children }: { children: ReactNode }) {
    return (
        <Pane pad="lg">
            <Stack gap="lg">{children}</Stack>
        </Pane>
    );
}

export function Card({
    title,
    children,
}: {
    title: string;
    children: ReactNode;
}) {
    return <GroupBox title={title}>{children}</GroupBox>;
}

export function Stats({ children }: { children: ReactNode }) {
    return (
        <GroupBox title="Summary">
            <Stack as="dl" direction="row" wrap gap="lg">
                {children}
            </Stack>
        </GroupBox>
    );
}

export function Stat({ label, value }: { label: string; value: ReactNode }) {
    return (
        <Stack gap="xxs" className="min-w-[120px] flex-1">
            <dt>{label}</dt>
            <dd>
                <Text strong>{value}</Text>
            </dd>
        </Stack>
    );
}

const INK: Record<ReturnType<typeof toneOf>, Tone | undefined> = {
    up: 'go',
    down: 'danger',
    flat: undefined,
};

export function Signed({ fraction }: { fraction: number }) {
    return <Text tone={INK[toneOf(fraction)]}>{signedPercent(fraction)}</Text>;
}

export function Fields({
    hidden,
    children,
}: {
    hidden?: boolean;
    children: ReactNode;
}) {
    return (
        <Stack
            direction="row"
            wrap
            align="end"
            gap="md"
            rowGap="sm"
            hidden={hidden}
        >
            {children}
        </Stack>
    );
}

export function Field({
    label,
    grow,
    children,
}: {
    label: string;
    grow?: boolean;
    children: ReactNode;
}) {
    return (
        <FieldRow
            label={label}
            layout="stacked"
            className={grow ? 'min-w-0 flex-[1_1_150px]' : 'flex-none'}
        >
            {children}
        </FieldRow>
    );
}

export function Actions({ children }: { children: ReactNode }) {
    return (
        <Stack direction="row" align="center" gap="sm">
            {children}
        </Stack>
    );
}

export function Rows({ children }: { children: ReactNode }) {
    return (
        <Stack as="ul" gap="xs">
            {children}
        </Stack>
    );
}

export function Row({ children }: { children: ReactNode }) {
    return (
        <Stack as="li" direction="row" align="center" gap="md">
            {children}
        </Stack>
    );
}

export function Jump({
    onClick,
    children,
}: {
    onClick: () => void;
    children: ReactNode;
}) {
    return (
        <Hyperlink
            onClick={(e) => {
                e.stopPropagation();
                onClick();
            }}
        >
            {children}
        </Hyperlink>
    );
}

export function Refusal({ error }: { error: ProblemDetails | null }) {
    return error ? (
        <Stack as="p" direction="row" align="center" gap="xs">
            <Icon name="error" />
            {refusal(error)}
        </Stack>
    ) : null;
}

export function Empty({ children }: { children: ReactNode }) {
    return <p>{children}</p>;
}
