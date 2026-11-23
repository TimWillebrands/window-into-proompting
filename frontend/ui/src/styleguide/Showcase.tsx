import type { ReactNode } from 'react';
import { type Scheme, schemes } from '../scheme';
import './styleguide.css';

export const schemeNames: Record<Scheme, string> = {
    'luna-blue': 'Luna Blue',
    noir: 'Noir',
};

export function Page({
    title,
    intro,
    children,
}: {
    title: string;
    intro?: ReactNode;
    children: ReactNode;
}) {
    return (
        <article className="ui-sg-page">
            <h1>{title}</h1>
            {intro && <p>{intro}</p>}
            {children}
        </article>
    );
}

export function Section({
    title,
    children,
}: {
    title: string;
    children: ReactNode;
}) {
    return (
        <section className="ui-sg-section">
            <h2>{title}</h2>
            {children}
        </section>
    );
}

export function SchemePair({
    children,
    className,
    attributes,
}: {
    children: (scheme: Scheme) => ReactNode;
    className?: string;
    attributes?: Record<string, string>;
}) {
    return (
        <div className="ui-sg-pair">
            {schemes.map((scheme) => (
                <div
                    key={scheme}
                    data-ui-scheme={scheme}
                    className={className ?? 'ui-sg-scheme'}
                    {...attributes}
                >
                    <h3>{schemeNames[scheme]}</h3>
                    {children(scheme)}
                </div>
            ))}
        </div>
    );
}

export function TokenName({ name }: { name: string }) {
    return <code className="ui-sg-label">{name}</code>;
}
