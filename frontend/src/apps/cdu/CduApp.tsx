import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import {
    getGetWorldsPartyIdCduQueryKey,
    useGetWorldsPartyIdCdu,
    useGetWorldsPartyIdCduLines,
} from '#api/party-zone';
import type { CduLineSummary, CduOverview } from '../../api/model';
import { useWorldEvents, WORLD_ID } from '../../lib/world';
import type { WorldEventData } from '../../lib/world-bus';
import {
    allowanceLine,
    exchangeWord,
    linePrefix,
    mergeLines,
    mindLine,
    prLine,
    SAID,
    shortSha,
} from './cdu-lib';
import './cdu.css';

interface CduFrame {
    lines?: CduLineSummary[];
}

/** The CDU's console: what's been logged so far, then whatever arrives live. */
function Terminal() {
    const [lines, setLines] = useState<CduLineSummary[]>([]);
    const end = useRef<HTMLDivElement>(null);
    const history = useGetWorldsPartyIdCduLines(WORLD_ID);

    useEffect(() => {
        if (history.data?.status === 200) {
            const fetched = history.data.data;
            setLines((held) => mergeLines(held, fetched));
        }
    }, [history.data]);

    useWorldEvents((envelope) => {
        const { kind, payload } = envelope.data as WorldEventData;
        if (kind !== 'cdu') return;
        const frame = payload as CduFrame;
        if (frame.lines) {
            const incoming = frame.lines;
            setLines((held) => mergeLines(held, incoming));
        }
    });

    const count = lines.length;
    useEffect(() => {
        if (count > 0) end.current?.scrollIntoView({ block: 'end' });
    }, [count]);

    return (
        <div className="cd-term" role="log" aria-live="polite">
            <div className="cd-line cd-muted">C:\CDU&gt; listen</div>
            {lines.map((l) => (
                <div key={l.n} className={`cd-line cd-${l.kind}`}>
                    <span className="cd-prompt">{linePrefix(l.kind)}</span>{' '}
                    {l.text}
                </div>
            ))}
            <div ref={end} />
        </div>
    );
}

/** Its state, its trunk and its PR: facts from git and GitHub. */
function State({ cdu }: { cdu: CduOverview }) {
    return (
        <>
            <p className="cd-state">
                {!cdu.up
                    ? 'The CDU is dark'
                    : cdu.awake
                      ? `Awake, hearing ${cdu.hearing ?? 'someone'}`
                      : 'Asleep until spoken to'}
            </p>
            {cdu.model ? <p className="cd-state">{cdu.model}</p> : null}
            <p className="cd-state">{allowanceLine(cdu.allowance)}</p>
            <h3 className="cd-section">Trunk</h3>
            {cdu.trunk.length === 0 ? (
                <p className="cd-state">Nothing the world lacks.</p>
            ) : (
                <ul className="cd-commits">
                    {cdu.trunk.map((c) => (
                        <li key={c.sha}>
                            <span className="cd-sha">{shortSha(c.sha)}</span>{' '}
                            {c.subject}{' '}
                            <span className="cd-sub">for {c.author}</span>
                        </li>
                    ))}
                </ul>
            )}
            {cdu.models.length > 0 ? (
                <>
                    <h3 className="cd-section">Minds, cheapest first</h3>
                    <ul className="cd-commits">
                        {cdu.models.map((m) => (
                            <li key={m.id}>
                                {mindLine(m)}
                                <span className="cd-sub">{m.label}</span>
                            </li>
                        ))}
                    </ul>
                </>
            ) : null}
            <h3 className="cd-section">Agora</h3>
            {cdu.pr ? (
                <p className="cd-state">
                    <a href={cdu.pr.url} target="_blank" rel="noreferrer">
                        {cdu.pr.title}
                    </a>
                    <span className="cd-sub">{prLine(cdu.pr)}</span>
                </p>
            ) : (
                <p className="cd-state">No PR open.</p>
            )}
        </>
    );
}

/** Who said what to it, and what it said back: its words, not facts. */
function Said({ cdu }: { cdu: CduOverview }) {
    if (cdu.exchanges.length === 0)
        return <p className="cd-state">Nobody has spoken to it yet.</p>;
    return (
        <ul className="cd-said">
            {cdu.exchanges.map((e) => (
                <li key={e.turnId}>
                    <span className="cd-title">
                        {e.speakerName} {SAID[e.kind] ?? 'said'}
                    </span>
                    {e.text}
                    <span className="cd-sub">
                        {exchangeWord(e)}
                        {e.model ? ' · woke ' + e.model : ''}
                    </span>
                    {e.reply ? (
                        <span className="cd-reply">{e.reply}</span>
                    ) : null}
                </li>
            ))}
        </ul>
    );
}

/** The Demiurge at work, live. Spectators see what only its Cytheurgists know. */
export default function CduApp() {
    const queryClient = useQueryClient();
    const overview = useGetWorldsPartyIdCdu(WORLD_ID);

    useWorldEvents((envelope) => {
        const { kind, payload } = envelope.data as WorldEventData;
        if (kind === 'cdu' && !(payload as CduFrame).lines)
            queryClient.invalidateQueries({
                queryKey: getGetWorldsPartyIdCduQueryKey(WORLD_ID),
            });
    });

    const cdu = overview.data?.status === 200 ? overview.data.data : null;
    if (!cdu) return <div className="cd-app cd-empty">Connecting…</div>;
    if (!cdu.enabled)
        return (
            <div className="cd-app cd-empty">
                No CDU is configured for this World.
            </div>
        );

    return (
        <div className="cd-app">
            <aside className="cd-list">
                <State cdu={cdu} />
                <h3 className="cd-section">Spoken to it</h3>
                <Said cdu={cdu} />
            </aside>
            <div className="cd-detail">
                <Terminal />
            </div>
        </div>
    );
}
