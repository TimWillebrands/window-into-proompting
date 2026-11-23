import {
    FieldRow,
    GroupBox,
    Hyperlink,
    Icon,
    type IconName,
    PageHeader,
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
        <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto p-3">
            {children}
        </div>
    );
}

export function Card({
    title,
    children,
}: {
    title: string;
    children: ReactNode;
}) {
    return (
        <GroupBox title={title} className="flex-none">
            {children}
        </GroupBox>
    );
}

export function Stats({ children }: { children: ReactNode }) {
    return (
        <GroupBox title="Summary" className="flex-none">
            <dl className="grid grid-cols-[repeat(auto-fill,minmax(120px,1fr))] gap-3">
                {children}
            </dl>
        </GroupBox>
    );
}

export function Stat({ label, value }: { label: string; value: ReactNode }) {
    return (
        <div className="flex flex-col gap-0.5">
            <dt>{label}</dt>
            <dd>
                <Text strong>{value}</Text>
            </dd>
        </div>
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
        <div
            className="flex flex-wrap items-end gap-x-2 gap-y-1.5"
            hidden={hidden}
        >
            {children}
        </div>
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
    return <div className="flex items-center gap-1.5">{children}</div>;
}

export function Rows({ children }: { children: ReactNode }) {
    return <ul className="grid gap-1">{children}</ul>;
}

export function Row({ children }: { children: ReactNode }) {
    return <li className="flex items-center gap-2">{children}</li>;
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
        <p className="flex items-center gap-1">
            <Icon name="error" />
            {refusal(error)}
        </p>
    ) : null;
}

export function Empty({ children }: { children: ReactNode }) {
    return <p>{children}</p>;
}
