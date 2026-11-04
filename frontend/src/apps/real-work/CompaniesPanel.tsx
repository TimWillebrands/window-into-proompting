import { useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import {
    getGetWorldsPartyIdCompaniesQueryKey,
    useDeleteWorldsPartyIdCompaniesCompanyIdStaffParticipantId,
    useGetWorldsPartyIdCompaniesSuspense,
    usePostWorldsPartyIdCompanies,
    usePostWorldsPartyIdCompaniesCompanyIdTrades,
    usePutWorldsPartyIdCompaniesCompanyIdStaffParticipantId,
} from '#api/party-zone';
import type { CompanySummary, JobSummary } from '../../api/model';
import { WORLD_ID } from '../../lib/world';
import {
    brierLabel,
    credits,
    refusal,
    returnLabel,
    tradeLabel,
} from './real-work-lib';

const cell = { padding: '2px 6px' } as const;
const failure = { color: '#a00', margin: '4px 0 0' } as const;

function useRefresh() {
    const queryClient = useQueryClient();
    return {
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdCompaniesQueryKey(WORLD_ID),
                }),
        },
    };
}

/** Companies (ADR 0030): start one, staff it, watch its Book, trade as anyone on the staff. */
export function CompaniesPanel({ people }: { people: JobSummary[] }) {
    const companies =
        useGetWorldsPartyIdCompaniesSuspense(WORLD_ID, {
            query: { refetchInterval: 30_000 },
        }).data.data ?? [];
    const [picked, setPicked] = useState<string | null>(null);
    const selected = companies.find((c) => c.id === picked) ?? null;

    return (
        <div className="flex flex-col" style={{ gap: 6 }}>
            {companies.length > 0 ? (
                <table
                    className="w-full"
                    style={{ borderCollapse: 'collapse' }}
                >
                    <thead>
                        <tr
                            style={{ background: '#ece9d8', textAlign: 'left' }}
                        >
                            <th style={cell}>Company</th>
                            <th style={cell}>Trades in</th>
                            <th style={cell}>Book</th>
                            <th style={cell}>Return</th>
                            <th style={cell}>Brier</th>
                        </tr>
                    </thead>
                    <tbody>
                        {companies.map((c) => (
                            <tr
                                key={c.id}
                                onClick={() => setPicked(c.id)}
                                aria-selected={c.id === picked}
                                style={{
                                    cursor: 'pointer',
                                    background:
                                        c.id === picked ? '#316ac5' : undefined,
                                    color: c.id === picked ? '#fff' : undefined,
                                }}
                            >
                                <td style={cell}>{c.name}</td>
                                <td style={cell}>
                                    {c.market}
                                    {c.focus ? `: ${c.focus}` : ''}
                                </td>
                                <td style={cell}>{credits(c.value)}</td>
                                <td style={cell}>{returnLabel(c)}</td>
                                <td style={cell}>{brierLabel(c.brier)}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            ) : null}
            {selected ? (
                <CompanyDetail
                    key={selected.id}
                    company={selected}
                    people={people}
                />
            ) : null}
            <FoundForm />
        </div>
    );
}

function CompanyDetail({
    company,
    people,
}: {
    company: CompanySummary;
    people: JobSummary[];
}) {
    return (
        <>
            <fieldset style={{ margin: 0 }}>
                <legend>{company.name}</legend>
                <p style={{ margin: '0 0 4px' }}>
                    {company.mandate} · {company.workplace} · cash{' '}
                    {credits(company.cash)}
                </p>
                {company.positions.map((p) => (
                    <div key={p.instrumentId}>
                        {p.title}: {credits(p.value)} (cost {credits(p.cost)})
                        {p.fresh ? '' : ' · stale'}
                    </div>
                ))}
                {company.positions.length === 0 ? (
                    <div style={{ color: '#666' }}>Nothing held.</div>
                ) : null}
            </fieldset>
            <StaffForm company={company} people={people} />
            {company.staff.length > 0 ? <TradeForm company={company} /> : null}
            {company.trades.length > 0 ? (
                <fieldset style={{ margin: 0 }}>
                    <legend>Trades</legend>
                    {company.trades.map((t) => (
                        <div key={t.id} title={t.thesis ?? undefined}>
                            {t.traderName ? `${t.traderName}: ` : ''}
                            {tradeLabel(t)} · {t.title}
                        </div>
                    ))}
                </fieldset>
            ) : null}
        </>
    );
}

function StaffForm({
    company,
    people,
}: {
    company: CompanySummary;
    people: JobSummary[];
}) {
    const ids = useId();
    const refresh = useRefresh();
    const hire =
        usePutWorldsPartyIdCompaniesCompanyIdStaffParticipantId(refresh);
    const dismiss =
        useDeleteWorldsPartyIdCompaniesCompanyIdStaffParticipantId(refresh);
    const hired = new Set(company.staff.map((s) => s.participantId));
    const free = people.filter((p) => !hired.has(p.participantId));
    const [who, setWho] = useState('');
    const [title, setTitle] = useState('trader');
    const [days, setDays] = useState('Mon-Fri');
    const [hours, setHours] = useState('09:00-17:00');
    const error = hire.error ?? dismiss.error;

    return (
        <fieldset style={{ margin: 0 }}>
            <legend>Staff</legend>
            {company.staff.map((s) => (
                <div
                    key={s.participantId}
                    className="flex items-center"
                    style={{ gap: 6 }}
                >
                    <span style={{ flex: 1 }}>
                        {s.name} · Brier {brierLabel(s.brier)}
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
                </div>
            ))}
            <form
                className="flex flex-wrap items-center"
                style={{ gap: 6, marginTop: 4 }}
                onSubmit={(e) => {
                    e.preventDefault();
                    hire.mutate({
                        partyId: WORLD_ID,
                        companyId: company.id,
                        participantId: who,
                        data: { title, days, hours },
                    });
                }}
            >
                <label htmlFor={`${ids}-w`}>Hire</label>
                <select
                    id={`${ids}-w`}
                    value={who}
                    onChange={(e) => setWho(e.currentTarget.value)}
                >
                    <option value="">someone…</option>
                    {free.map((p) => (
                        <option key={p.participantId} value={p.participantId}>
                            {p.name}
                        </option>
                    ))}
                </select>
                <label htmlFor={`${ids}-t`}>as</label>
                <input
                    id={`${ids}-t`}
                    value={title}
                    onChange={(e) => setTitle(e.currentTarget.value)}
                    style={{ width: 100 }}
                />
                <input
                    aria-label="Days"
                    value={days}
                    onChange={(e) => setDays(e.currentTarget.value)}
                    style={{ width: 70 }}
                />
                <input
                    aria-label="Hours"
                    value={hours}
                    onChange={(e) => setHours(e.currentTarget.value)}
                    style={{ width: 90 }}
                />
                <button type="submit" disabled={!who || hire.isPending}>
                    Hire
                </button>
            </form>
            {error ? <p style={failure}>{refusal(error)}</p> : null}
        </fieldset>
    );
}

/** The same order a Persona's Life places, for whoever on the staff you pick. */
function TradeForm({ company }: { company: CompanySummary }) {
    const ids = useId();
    const trade = usePostWorldsPartyIdCompaniesCompanyIdTrades(useRefresh());
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
    const isQuestion = company.market === 'predictions';

    return (
        <fieldset style={{ margin: 0 }}>
            <legend>Trade</legend>
            <form
                className="flex flex-wrap items-center"
                style={{ gap: 6 }}
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
                <select
                    aria-label="Trader"
                    value={trader}
                    onChange={(e) => setTrader(e.currentTarget.value)}
                >
                    <option value="">as…</option>
                    {company.staff.map((s) => (
                        <option key={s.participantId} value={s.participantId}>
                            {s.name}
                        </option>
                    ))}
                </select>
                <select
                    aria-label="Side"
                    value={side}
                    onChange={(e) => setSide(e.currentTarget.value)}
                >
                    <option value="buy">buy</option>
                    <option value="sell">sell</option>
                </select>
                <select
                    aria-label="Instrument"
                    value={instrument}
                    onChange={(e) => setInstrument(e.currentTarget.value)}
                    style={{ flex: 1, minWidth: 160 }}
                >
                    <option value="">what…</option>
                    {[...held, ...watched].map((i) => (
                        <option key={i.id} value={i.id}>
                            {i.title}
                        </option>
                    ))}
                </select>
                <label htmlFor={`${ids}-p`}>%</label>
                <input
                    id={`${ids}-p`}
                    value={percent}
                    onChange={(e) => setPercent(e.currentTarget.value)}
                    style={{ width: 40 }}
                />
                {isQuestion ? (
                    <input
                        aria-label="Your percent chance it pays out"
                        placeholder="chance %"
                        value={chance}
                        onChange={(e) => setChance(e.currentTarget.value)}
                        style={{ width: 70 }}
                    />
                ) : null}
                <input
                    aria-label="Thesis"
                    placeholder="why"
                    value={thesis}
                    onChange={(e) => setThesis(e.currentTarget.value)}
                    style={{ flex: 1, minWidth: 120 }}
                />
                <button
                    type="submit"
                    disabled={!trader || !instrument || trade.isPending}
                >
                    Trade
                </button>
            </form>
            {trade.error ? <p style={failure}>{refusal(trade.error)}</p> : null}
        </fieldset>
    );
}

function FoundForm() {
    const ids = useId();
    const found = usePostWorldsPartyIdCompanies(useRefresh());
    const [name, setName] = useState('');
    const [mandate, setMandate] = useState('');
    const [market, setMarket] = useState('predictions');
    const [focus, setFocus] = useState('');
    const [capital, setCapital] = useState('100000');

    return (
        <fieldset style={{ margin: 0 }}>
            <legend>Start a company</legend>
            <form
                className="flex flex-wrap items-center"
                style={{ gap: 6 }}
                onSubmit={(e) => {
                    e.preventDefault();
                    found.mutate({
                        partyId: WORLD_ID,
                        data: {
                            name,
                            mandate,
                            market,
                            focus: focus || null,
                            capital: Number(capital),
                        },
                    });
                }}
            >
                <input
                    aria-label="Name"
                    placeholder="Polder Capital"
                    value={name}
                    onChange={(e) => setName(e.currentTarget.value)}
                    style={{ width: 130 }}
                />
                <input
                    aria-label="Mandate"
                    placeholder="trades AI questions"
                    value={mandate}
                    onChange={(e) => setMandate(e.currentTarget.value)}
                    style={{ flex: 1, minWidth: 140 }}
                />
                <label htmlFor={`${ids}-m`}>in</label>
                <select
                    id={`${ids}-m`}
                    value={market}
                    onChange={(e) => setMarket(e.currentTarget.value)}
                >
                    <option value="predictions">predictions</option>
                    <option value="crypto">crypto</option>
                    <option value="stocks">stocks</option>
                </select>
                <input
                    aria-label="Focus"
                    placeholder="AI · BTC, ETH · semiconductors"
                    value={focus}
                    onChange={(e) => setFocus(e.currentTarget.value)}
                    style={{ width: 170 }}
                />
                <input
                    aria-label="Capital"
                    value={capital}
                    onChange={(e) => setCapital(e.currentTarget.value)}
                    style={{ width: 70 }}
                />
                <button
                    type="submit"
                    disabled={
                        !name.trim() || !mandate.trim() || found.isPending
                    }
                >
                    Start
                </button>
            </form>
            {found.error ? <p style={failure}>{refusal(found.error)}</p> : null}
        </fieldset>
    );
}
