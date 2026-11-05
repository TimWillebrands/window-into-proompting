import type { ReactNode } from 'react';
import type { ProblemDetails } from '../../api/model';
import { refusal, type Tone } from './jobs-lib';

/** The blue band at the top of every page. */
export function PageHead({
    icon,
    title,
    sub,
    chip,
}: {
    icon: string;
    title: string;
    sub?: string;
    chip?: string;
}) {
    return (
        <header className="wk-head">
            <span className="wk-head-icon" aria-hidden>
                {icon}
            </span>
            <div className="wk-head-text">
                <h2>{title}</h2>
                {sub ? <p>{sub}</p> : null}
            </div>
            {chip ? <span className="wk-chip">{chip}</span> : null}
        </header>
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
        <section className="wk-card">
            <h3>{title}</h3>
            <div className="wk-card-body">{children}</div>
        </section>
    );
}

export function Stat({
    label,
    value,
    tone,
}: {
    label: string;
    value: string;
    tone?: Tone;
}) {
    return (
        <div className="wk-stat">
            <span>{label}</span>
            <strong data-tone={tone}>{value}</strong>
        </div>
    );
}

/** A label above its control; wrapping it keeps them tied without ids. */
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
        // biome-ignore lint/a11y/noLabelWithoutControl: the control arrives as children
        <label className="wk-field" data-grow={grow || undefined}>
            <span>{label}</span>
            {children}
        </label>
    );
}

/** Looks like text, works like a link: how every page reaches the others. */
export function Jump({
    onClick,
    children,
}: {
    onClick: () => void;
    children: ReactNode;
}) {
    return (
        <button
            type="button"
            className="xp-bare wk-jump"
            onClick={(e) => {
                e.stopPropagation();
                onClick();
            }}
        >
            {children}
        </button>
    );
}

export function Refusal({ error }: { error: ProblemDetails | null }) {
    return error ? <p className="wk-refusal">{refusal(error)}</p> : null;
}

export function Empty({ children }: { children: ReactNode }) {
    return <p className="wk-empty">{children}</p>;
}
