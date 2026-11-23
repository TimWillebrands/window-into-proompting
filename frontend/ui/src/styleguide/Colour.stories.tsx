import type { Meta, StoryObj } from '@storybook/react-vite';
import { useLayoutEffect, useRef, useState } from 'react';
import type { Scheme } from '../scheme';
import { worstRatio } from './contrast';
import { Page, SchemePair, Section, TokenName } from './Showcase';
import {
    type ContrastPair,
    colourGroups,
    contrastPairs,
    seriesTokens,
} from './token-list';

const meta: Meta = {
    title: 'Styleguide/Colour',
    parameters: { layout: 'padded' },
};

export default meta;

function SwatchGrid() {
    return (
        <SchemePair>
            {() =>
                colourGroups.map((group) => (
                    <div key={group.title} className="ui-sg-section">
                        <h3>{group.title}</h3>
                        <div className="ui-sg-swatches">
                            {group.tokens.map((token) => (
                                <div key={token} className="ui-sg-swatch">
                                    <div
                                        className="ui-sg-chip"
                                        style={{ background: `var(${token})` }}
                                    />
                                    <TokenName name={token} />
                                </div>
                            ))}
                        </div>
                    </div>
                ))
            }
        </SchemePair>
    );
}

const useRootAttributes = () => {
    const [version, setVersion] = useState(0);
    useLayoutEffect(() => {
        const observer = new MutationObserver(() => setVersion((v) => v + 1));
        observer.observe(document.documentElement, { attributes: true });
        return () => observer.disconnect();
    }, []);
    return version;
};

function PairRow({
    pair,
    scheme,
    version,
}: {
    pair: ContrastPair;
    scheme: Scheme;
    version: number;
}) {
    const sample = useRef<HTMLSpanElement>(null);
    const [value, setValue] = useState<number>();
    useLayoutEffect(() => {
        if (version < 0 || !sample.current) return;
        const el = sample.current;
        const measure = () => setValue(worstRatio(el));
        measure();
        document.fonts?.ready.then(measure);
    }, [version]);
    const needed = pair.large ? 3 : 4.5;
    const verdict = pair.exempt
        ? 'exempt'
        : value === undefined
          ? 'unknown'
          : value >= needed
            ? 'pass'
            : 'fail';
    const attributes = pair.solid
        ? { 'data-ui-scheme': scheme, 'data-ui-reduce-transparency': '' }
        : {};
    return (
        <tr>
            <td>
                <span {...attributes}>
                    <span
                        ref={sample}
                        className="ui-sg-sample"
                        style={{
                            background: `var(${pair.surface})`,
                            color: `var(${pair.text})`,
                        }}
                    >
                        Aa Text
                    </span>
                </span>
            </td>
            <td>
                <TokenName name={pair.text} />
                <br />
                <TokenName name={`on ${pair.surface}`} />
            </td>
            <td>{value === undefined ? '…' : `${value.toFixed(2)}:1`}</td>
            <td>
                <span
                    className={
                        verdict === 'pass'
                            ? 'ui-sg-pass'
                            : verdict === 'exempt'
                              ? 'ui-sg-exempt'
                              : 'ui-sg-fail'
                    }
                >
                    {verdict === 'exempt'
                        ? `exempt: ${pair.exempt}`
                        : `${verdict} (needs ${needed}${pair.large ? ', large' : ''})`}
                </span>
            </td>
        </tr>
    );
}

function ContrastTable() {
    const version = useRootAttributes();
    return (
        <SchemePair>
            {(scheme) => (
                <table className="ui-sg-table">
                    <thead>
                        <tr>
                            <th>Sample</th>
                            <th>Pair</th>
                            <th>Ratio</th>
                            <th>WCAG AA</th>
                        </tr>
                    </thead>
                    <tbody>
                        {contrastPairs.map((pair) => (
                            <PairRow
                                key={`${pair.text}${pair.surface}`}
                                pair={pair}
                                scheme={scheme}
                                version={version}
                            />
                        ))}
                    </tbody>
                </table>
            )}
        </SchemePair>
    );
}

export const Swatches: StoryObj = {
    render: () => (
        <Page
            title="Colour"
            intro="Every colour token, grouped as in the token contract, in both schemes. Gradients are tokens too."
        >
            <SwatchGrid />
        </Page>
    ),
};

export const Contrast: StoryObj = {
    render: () => (
        <Page
            title="Contrast"
            intro="Text tokens on the surfaces they sit on, measured live from the computed tokens. Gradients are measured at their worst point across the middle band (25% to 75%), glass on its solid fallback. Toggle Contrast in the toolbar to re-measure."
        >
            <Section title="Text on surfaces">
                <ContrastTable />
            </Section>
        </Page>
    ),
};

const shares = [34, 22, 16, 11, 7, 5, 3, 2, 1];

function SeriesOn({ surface }: { surface: 'face' | 'content' }) {
    return (
        <div className="ui-sg-series" data-surface={surface}>
            <TokenName name={`on --ui-${surface}`} />
            <div className="ui-sg-series-bar">
                {seriesTokens.map((token, i) => (
                    <span
                        key={token}
                        style={{
                            background: `var(${token})`,
                            flexGrow: shares[i],
                        }}
                    />
                ))}
            </div>
            <div className="ui-sg-series-dots">
                {seriesTokens.map((token) => (
                    <span key={token} className="ui-sg-series-key">
                        <span
                            className="ui-sg-series-dot"
                            style={{ background: `var(${token})` }}
                        />
                        <TokenName name={token.replace('--ui-series-', '')} />
                    </span>
                ))}
            </div>
        </div>
    );
}

export const Series: StoryObj = {
    render: () => (
        <Page
            title="Series"
            intro="A categorical palette for data: provider shares, person colours, graph node kinds. Apps pass these as data colours through a part's colour prop or a --ui-* custom property, in fixed order, never cycled; a ninth category folds into --ui-series-other. Validated for colour-vision deficiency on adjacent pairs in both schemes; only the first three stay apart when every pair can touch (scatter, graphs), so beyond three, label the marks. Identity is never colour alone: keep a legend or labels."
        >
            <SchemePair>
                {() => (
                    <>
                        <SeriesOn surface="face" />
                        <SeriesOn surface="content" />
                    </>
                )}
            </SchemePair>
        </Page>
    ),
};
