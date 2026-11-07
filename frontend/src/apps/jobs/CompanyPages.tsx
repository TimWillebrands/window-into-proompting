import { useState } from 'react';
import {
    useDeleteWorldsPartyIdCompaniesCompanyIdStaffParticipantId,
    usePostWorldsPartyIdCompanies,
    usePostWorldsPartyIdCompaniesCompanyIdTrades,
    usePutWorldsPartyIdCompaniesCompanyIdStaffParticipantId,
} from '#api/party-zone';
import type { CompanySummary } from '../../api/model';
import { WORLD_ID } from '../../lib/world';
import {
    bookReturn,
    brierLabel,
    credits,
    dayMove,
    midOf,
    positionReturn,
    priceLabel,
    signedPercent,
    toneOf,
    tradeLabel,
    whenLabel,
} from './jobs-lib';
import { Card, Empty, Field, Jump, PageHead, Refusal, Stat } from './parts';
import { useRefreshWork, useWork } from './work-context';

export function CompaniesPage() {
    const { companies, go } = useWork();
    return (
        <>
            <PageHead
                icon="🏦"
                title="Companies"
                sub="Real prices, pretend money. Their books never touch anyone's wallet."
            />
            <div className="wk-page">
                {companies.length === 0 ? (
                    <Empty>No companies yet. Start the first one below.</Empty>
                ) : (
                    <div className="wk-tiles">
                        {companies.map((c) => {
                            const change = bookReturn(c);
                            return (
                                <button
                                    key={c.id}
                                    type="button"
                                    className="xp-bare wk-tile"
                                    onClick={() =>
                                        go({ page: 'company', id: c.id })
                                    }
                                >
                                    <strong>{c.name}</strong>
                                    <span>{c.mandate}</span>
                                    <span>
                                        {credits(c.value)} credits ·{' '}
                                        <b data-tone={toneOf(change)}>
                                            {signedPercent(change)}
                                        </b>{' '}
                                        · {c.staff.length} staff
                                    </span>
                                </button>
                            );
                        })}
                    </div>
                )}
                <FoundForm />
            </div>
        </>
    );
}

function FoundForm() {
    const { go } = useWork();
    const refresh = useRefreshWork();
    const found = usePostWorldsPartyIdCompanies(refresh);
    const [name, setName] = useState('');
    const [mandate, setMandate] = useState('');
    const [kind, setKind] = useState('trading');
    const [market, setMarket] = useState('predictions');
    const [focus, setFocus] = useState('');
    const [capital, setCapital] = useState('100000');

    return (
        <Card title="Start a company">
            <form
                onSubmit={(e) => {
                    e.preventDefault();
                    found.mutate(
                        {
                            partyId: WORLD_ID,
                            data: {
                                name,
                                mandate,
                                market,
                                focus: focus || null,
                                capital: Number(capital),
                                kind,
                            },
                        },
                        {
                            onSuccess: (res) => {
                                if (res.status === 200)
                                    go({ page: 'company', id: res.data.id });
                            },
                        },
                    );
                }}
            >
                <div className="wk-fields">
                    <Field label="Name" grow>
                        <input
                            placeholder="Polder Capital"
                            value={name}
                            onChange={(e) => setName(e.currentTarget.value)}
                        />
                    </Field>
                    <Field label="What it does" grow>
                        <input
                            placeholder="trades AI questions"
                            value={mandate}
                            onChange={(e) => setMandate(e.currentTarget.value)}
                        />
                    </Field>
                </div>
                <div className="wk-fields">
                    <Field label="Kind">
                        <select
                            value={kind}
                            onChange={(e) => setKind(e.currentTarget.value)}
                        >
                            <option value="trading">trading</option>
                            <option value="works">works on the world</option>
                        </select>
                    </Field>
                </div>
                <div className="wk-fields" hidden={kind === 'works'}>
                    <Field label="Market">
                        <select
                            value={market}
                            onChange={(e) => setMarket(e.currentTarget.value)}
                        >
                            <option value="predictions">predictions</option>
                            <option value="crypto">crypto</option>
                            <option value="stocks">stocks</option>
                        </select>
                    </Field>
                    <Field label="Focus" grow>
                        <input
                            placeholder="AI · BTC, ETH · semiconductors"
                            value={focus}
                            onChange={(e) => setFocus(e.currentTarget.value)}
                        />
                    </Field>
                    <Field label="Capital">
                        <input
                            className="wk-short"
                            value={capital}
                            onChange={(e) => setCapital(e.currentTarget.value)}
                        />
                    </Field>
                </div>
                <div className="wk-actions">
                    <button
                        type="submit"
                        disabled={
                            !name.trim() || !mandate.trim() || found.isPending
                        }
                    >
                        Start
                    </button>
                </div>
                <Refusal error={found.error} />
            </form>
        </Card>
    );
}

/** No Book here: the work happens on the bench, in The Works window. */
function WorksPage({ company }: { company: CompanySummary }) {
    return (
        <>
            <PageHead icon="🏗️" title={company.name} sub={company.mandate} />
            <div className="wk-page">
                <Staff company={company} />
            </div>
        </>
    );
}

export function CompanyPage({ company }: { company: CompanySummary }) {
    if (company.kind === 'works') return <WorksPage company={company} />;
    const change = bookReturn(company);
    const market = company.focus
        ? `${company.market}: ${company.focus}`
        : company.market;
    return (
        <>
            <PageHead
                icon="🏦"
                title={company.name}
                sub={[
                    company.mandate,
                    market,
                    company.workplace === company.name
                        ? null
                        : company.workplace,
                ]
                    .filter(Boolean)
                    .join(' · ')}
            />
            <div className="wk-page">
                <div className="wk-stats">
                    <Stat label="Book" value={credits(company.value)} />
                    <Stat
                        label="Return"
                        value={signedPercent(change)}
                        tone={toneOf(change)}
                    />
                    <Stat label="Cash" value={credits(company.cash)} />
                    <Stat label="Brier" value={brierLabel(company.brier)} />
                </div>
                <Staff company={company} />
                <Holdings company={company} />
                <Watching company={company} />
                {company.staff.length > 0 ? (
                    <TradeForm company={company} />
                ) : null}
                {company.trades.length > 0 ? (
                    <Card title="Trades">
                        <ul className="wk-log">
                            {company.trades.map((t) => (
                                <li key={t.id} title={t.thesis ?? undefined}>
                                    <time>{whenLabel(t.at)}</time>
                                    <span>
                                        {t.traderName
                                            ? `${t.traderName}: `
                                            : ''}
                                        {tradeLabel(t)} · {t.title}
                                    </span>
                                </li>
                            ))}
                        </ul>
                    </Card>
                ) : null}
            </div>
        </>
    );
}

function Holdings({ company }: { company: CompanySummary }) {
    return (
        <Card title="Holding">
            {company.positions.length === 0 ? (
                <Empty>Nothing held.</Empty>
            ) : (
                <table className="wk-table">
                    <thead>
                        <tr>
                            <th>What</th>
                            <th className="wk-num">Worth</th>
                            <th className="wk-num">Return</th>
                        </tr>
                    </thead>
                    <tbody>
                        {company.positions.map((p) => {
                            const change = positionReturn(p);
                            return (
                                <tr key={p.instrumentId} data-still>
                                    <td>
                                        {p.title}
                                        {p.fresh ? '' : ' (stale)'}
                                    </td>
                                    <td className="wk-num">
                                        {credits(p.value)}
                                    </td>
                                    <td
                                        className="wk-num"
                                        data-tone={toneOf(change)}
                                    >
                                        {signedPercent(change)}
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            )}
        </Card>
    );
}

function Watching({ company }: { company: CompanySummary }) {
    const open = company.watchlist.filter((w) => w.open);
    return (
        <Card title="Watching">
            {open.length === 0 ? (
                <Empty>The market is quiet right now.</Empty>
            ) : (
                <table className="wk-table">
                    <thead>
                        <tr>
                            <th>What</th>
                            <th className="wk-num">Price</th>
                            <th className="wk-num">Today</th>
                        </tr>
                    </thead>
                    <tbody>
                        {open.map((w) => {
                            const move = dayMove(w);
                            return (
                                <tr key={w.instrumentId} data-still>
                                    <td>{w.title}</td>
                                    <td className="wk-num">
                                        {priceLabel(midOf(w))}
                                    </td>
                                    <td
                                        className="wk-num"
                                        data-tone={
                                            move === null
                                                ? undefined
                                                : toneOf(move)
                                        }
                                    >
                                        {move === null
                                            ? ''
                                            : signedPercent(move)}
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            )}
        </Card>
    );
}

function Staff({ company }: { company: CompanySummary }) {
    const { people, go } = useWork();
    const refresh = useRefreshWork();
    const hire =
        usePutWorldsPartyIdCompaniesCompanyIdStaffParticipantId(refresh);
    const dismiss =
        useDeleteWorldsPartyIdCompaniesCompanyIdStaffParticipantId(refresh);
    const [who, setWho] = useState('');
    const hired = new Set(company.staff.map((s) => s.participantId));
    const free = people.filter((p) => !hired.has(p.participantId));

    return (
        <Card title="Staff">
            {company.staff.length === 0 ? (
                <Empty>Nobody works here yet.</Empty>
            ) : (
                <ul className="wk-rows">
                    {company.staff.map((s) => (
                        <li key={s.participantId}>
                            <Jump
                                onClick={() =>
                                    go({ page: 'person', id: s.participantId })
                                }
                            >
                                {s.name}
                            </Jump>
                            <span className="wk-muted">
                                Brier {brierLabel(s.brier)}
                            </span>
                            <button
                                type="button"
                                disabled={dismiss.isPending}
                                onClick={() =>
                                    dismiss.mutate({
                                        partyId: WORLD_ID,
                                        companyId: company.id,
                                        participantId: s.participantId,
                                    })
                                }
                            >
                                Let go
                            </button>
                        </li>
                    ))}
                </ul>
            )}
            <form
                className="wk-inline"
                onSubmit={(e) => {
                    e.preventDefault();
                    hire.mutate(
                        {
                            partyId: WORLD_ID,
                            companyId: company.id,
                            participantId: who,
                            data: {
                                title: 'trader',
                                days: 'Mon-Fri',
                                hours: '09:00-17:00',
                            },
                        },
                        { onSuccess: () => setWho('') },
                    );
                }}
            >
                <select
                    aria-label="Who to hire"
                    value={who}
                    onChange={(e) => setWho(e.currentTarget.value)}
                >
                    <option value="">Hire someone…</option>
                    {free.map((p) => (
                        <option key={p.participantId} value={p.participantId}>
                            {p.name}
                            {p.title ? ` (now: ${p.title})` : ''}
                        </option>
                    ))}
                </select>
                <button type="submit" disabled={!who || hire.isPending}>
                    Hire
                </button>
            </form>
            <Refusal error={hire.error ?? dismiss.error} />
        </Card>
    );
}

/** The same order a Persona's Life places, for whoever on the staff you pick. */
function TradeForm({ company }: { company: CompanySummary }) {
    const trade = usePostWorldsPartyIdCompaniesCompanyIdTrades(
        useRefreshWork(),
    );
    const [trader, setTrader] = useState('');
    const [instrument, setInstrument] = useState('');
    const [side, setSide] = useState('buy');
    const [percent, setPercent] = useState('5');
    const [chance, setChance] = useState('');
    const [thesis, setThesis] = useState('');
    const held = company.positions.map((p) => ({
        id: p.instrumentId,
        title: `${p.title} (held)`,
    }));
    const watched = company.watchlist
        .filter(
            (q) =>
                q.open &&
                !company.positions.some(
                    (p) => p.instrumentId === q.instrumentId,
                ),
        )
        .map((q) => ({ id: q.instrumentId, title: q.title }));

    return (
        <Card title="Trade">
            <form
                onSubmit={(e) => {
                    e.preventDefault();
                    trade.mutate({
                        partyId: WORLD_ID,
                        companyId: company.id,
                        data: {
                            traderId: trader,
                            instrumentId: instrument,
                            side,
                            percent: Number(percent),
                            thesis: thesis || null,
                            chance: chance === '' ? null : Number(chance),
                        },
                    });
                }}
            >
                <div className="wk-fields">
                    <Field label="As">
                        <select
                            value={trader}
                            onChange={(e) => setTrader(e.currentTarget.value)}
                        >
                            <option value="">Who…</option>
                            {company.staff.map((s) => (
                                <option
                                    key={s.participantId}
                                    value={s.participantId}
                                >
                                    {s.name}
                                </option>
                            ))}
                        </select>
                    </Field>
                    <Field label="Side">
                        <select
                            value={side}
                            onChange={(e) => setSide(e.currentTarget.value)}
                        >
                            <option value="buy">buy</option>
                            <option value="sell">sell</option>
                        </select>
                    </Field>
                    <Field label="What" grow>
                        <select
                            value={instrument}
                            onChange={(e) =>
                                setInstrument(e.currentTarget.value)
                            }
                        >
                            <option value="">Pick one…</option>
                            {[...held, ...watched].map((i) => (
                                <option key={i.id} value={i.id}>
                                    {i.title}
                                </option>
                            ))}
                        </select>
                    </Field>
                </div>
                <div className="wk-fields">
                    <Field label="Percent">
                        <input
                            className="wk-tiny"
                            value={percent}
                            onChange={(e) => setPercent(e.currentTarget.value)}
                        />
                    </Field>
                    {company.market === 'predictions' ? (
                        <Field label="Chance %">
                            <input
                                className="wk-tiny"
                                value={chance}
                                onChange={(e) =>
                                    setChance(e.currentTarget.value)
                                }
                            />
                        </Field>
                    ) : null}
                    <Field label="Why" grow>
                        <input
                            value={thesis}
                            onChange={(e) => setThesis(e.currentTarget.value)}
                        />
                    </Field>
                </div>
                <div className="wk-actions">
                    <button
                        type="submit"
                        disabled={!trader || !instrument || trade.isPending}
                    >
                        Trade
                    </button>
                </div>
                <Refusal error={trade.error} />
            </form>
        </Card>
    );
}
