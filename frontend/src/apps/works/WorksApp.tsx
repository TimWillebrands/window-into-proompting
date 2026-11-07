import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import {
    getGetWorldsPartyIdWorksQueryKey,
    useGetWorldsPartyIdWorks,
    useGetWorldsPartyIdWorksCommissionsCommissionIdLines,
    usePostWorldsPartyIdWorksCommissionsCommissionIdReject,
} from '#api/party-zone';
import type { CommissionSummary, WorksLineSummary } from '../../api/model';
import { useIsAdmin, useWorldEvents, WORLD_ID } from '../../lib/world';
import type { WorldEventData } from '../../lib/world-bus';
import {
    factsLine,
    linePrefix,
    mergeCommand,
    mergeLines,
    money,
    pickDefault,
    statusWord,
} from './works-lib';
import './works.css';

interface WorksFrame {
    commissionId: string;
    status: string;
    lines: WorksLineSummary[];
}

/** The builder's terminal: what's been logged so far, then whatever arrives live. */
function Terminal({ commission }: { commission: CommissionSummary }) {
    const [lines, setLines] = useState<WorksLineSummary[]>([]);
    const end = useRef<HTMLDivElement>(null);
    const history = useGetWorldsPartyIdWorksCommissionsCommissionIdLines(
        WORLD_ID,
        commission.id,
    );

    useEffect(() => {
        if (history.data?.status === 200) {
            const fetched = history.data.data;
            setLines((held) => mergeLines(held, fetched));
        }
    }, [history.data]);

    useWorldEvents((envelope) => {
        const { kind, payload } = envelope.data as WorldEventData;
        if (kind !== 'works') return;
        const frame = payload as WorksFrame;
        if (frame.commissionId !== commission.id) return;
        setLines((held) => mergeLines(held, frame.lines));
    });

    const count = lines.length;
    useEffect(() => {
        if (count > 0) end.current?.scrollIntoView({ block: 'end' });
    }, [count]);

    return (
        <div className="wr-term" role="log" aria-live="polite">
            <div className="wr-line wr-muted">
                C:\WORKS&gt; commission "{commission.title}"
            </div>
            {lines.map((l) => (
                <div key={l.n} className={`wr-line wr-${l.kind}`}>
                    <span className="wr-prompt">{linePrefix(l.kind)}</span>{' '}
                    {l.text}
                </div>
            ))}
            <div ref={end} />
        </div>
    );
}

/** What the world-spirits need: the patch, and a way to say no. */
function Spirits({ commission }: { commission: CommissionSummary }) {
    const admin = useIsAdmin();
    const queryClient = useQueryClient();
    const [note, setNote] = useState('');
    const reject = usePostWorldsPartyIdWorksCommissionsCommissionIdReject({
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdWorksQueryKey(WORLD_ID),
                }),
        },
    });
    return (
        <div className="wr-spirits">
            <strong>Proposition {commission.proposition}</strong>
            <code className="wr-merge">
                {mergeCommand(window.location.origin, WORLD_ID, commission.id)}
            </code>
            {admin ? (
                <form
                    className="wr-reject"
                    onSubmit={(e) => {
                        e.preventDefault();
                        reject.mutate({
                            partyId: WORLD_ID,
                            commissionId: commission.id,
                            data: { note: note || null },
                        });
                    }}
                >
                    <input
                        placeholder="The spirits' word (optional)"
                        value={note}
                        onChange={(e) => setNote(e.currentTarget.value)}
                    />
                    <button type="submit" disabled={reject.isPending}>
                        Turn down
                    </button>
                </form>
            ) : null}
        </div>
    );
}

function Details({ commission }: { commission: CommissionSummary }) {
    return (
        <div className="wr-detail">
            <header className="wr-head">
                <h2>{commission.title}</h2>
                <p>
                    {commission.architectName} · {statusWord(commission.status)}{' '}
                    · {factsLine(commission)}
                </p>
            </header>
            <p className="wr-why">{commission.why}</p>
            <Terminal key={commission.id} commission={commission} />
            {commission.checks.length > 0 ? (
                <ul className="wr-checks">
                    {commission.checks.map((c) => (
                        <li
                            key={c.name}
                            className={c.passed ? 'wr-ok' : 'wr-bad'}
                        >
                            {c.passed ? '✓' : '✗'} {c.name} {c.seconds}s{' '}
                            {c.detail}
                        </li>
                    ))}
                </ul>
            ) : null}
            {commission.status === 'Proposed' ? (
                <Spirits commission={commission} />
            ) : null}
            {commission.note ? (
                <p className="wr-why">The spirits: {commission.note}</p>
            ) : null}
        </div>
    );
}

/** What the builder is doing to the world, live. Spectators see what staff keep secret. */
export default function WorksApp() {
    const queryClient = useQueryClient();
    const overview = useGetWorldsPartyIdWorks(WORLD_ID);
    const [picked, setPicked] = useState<string | null>(null);

    useWorldEvents((envelope) => {
        const { kind } = envelope.data as WorldEventData;
        if (kind === 'works')
            queryClient.invalidateQueries({
                queryKey: getGetWorldsPartyIdWorksQueryKey(WORLD_ID),
            });
    });

    const works = overview.data?.status === 200 ? overview.data.data : null;
    if (!works) return <div className="wr-app wr-empty">Connecting…</div>;
    if (!works.enabled)
        return (
            <div className="wr-app wr-empty">
                No Runner is configured for this World.
            </div>
        );

    const chosen = picked ?? pickDefault(works.commissions);
    const shown = works.commissions.find((c) => c.id === chosen);

    return (
        <div className="wr-app">
            <aside className="wr-list">
                <p className="wr-state">
                    {works.builderUp
                        ? works.builderBusy
                            ? 'The builder is working'
                            : 'The builder is free'
                        : 'The builder is not answering'}
                </p>
                <p className="wr-state">
                    Reserve:{' '}
                    {works.reserveUsd == null
                        ? 'unreadable'
                        : money(works.reserveUsd)}
                </p>
                {works.commissions.length === 0 ? (
                    <p className="wr-state">Nothing commissioned yet.</p>
                ) : null}
                {works.commissions.map((c) => (
                    <button
                        key={c.id}
                        type="button"
                        className={
                            c.id === chosen ? 'wr-item wr-picked' : 'wr-item'
                        }
                        onClick={() => setPicked(c.id)}
                    >
                        <span className="wr-title">{c.title}</span>
                        <span className="wr-sub">
                            {c.architectName} · {statusWord(c.status)}
                        </span>
                    </button>
                ))}
            </aside>
            {shown ? (
                <Details commission={shown} />
            ) : (
                <div className="wr-detail" />
            )}
        </div>
    );
}
