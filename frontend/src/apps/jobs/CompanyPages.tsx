import {
    ComboBox,
    CommandButton,
    Icon,
    ListView,
    Stack,
    TextBox,
} from '@partytown/ui';
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
    tradeLabel,
    whenLabel,
} from './jobs-lib';
import {
    Actions,
    Card,
    Empty,
    Field,
    Fields,
    Jump,
    Page,
    PageHead,
    Refusal,
    Row,
    Rows,
    Signed,
    Stat,
    Stats,
} from './parts';
import { useRefreshWork, useWork } from './work-context';

export function CompaniesPage() {
    const { companies, go } = useWork();
    return (
        <>
            <PageHead
                icon="company"
                title="Companies"
                sub="Real prices, pretend money. Their books never touch anyone's wallet."
            />
            <Page>
                {companies.length === 0 ? (
                    <Empty>No companies yet. Start the first one below.</Empty>
                ) : (
                    <ListView
                        view="tiles"
                        label="Companies"
                        activateOn="single"
                        onActivate={(id) => go({ page: 'company', id })}
                        items={companies.map((c) => ({
                            id: c.id,
                            label: c.name,
                            largeIcon: <Icon name="company" size={48} />,
                            details: [
                                c.mandate,
                                <>
                                    {credits(c.value)} credits ·{' '}
                                    <Signed fraction={bookReturn(c)} /> ·{' '}
                                    {c.staff.length} staff
                                </>,
                            ],
                        }))}
                    />
                )}
                <FoundForm />
            </Page>
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
            <Stack
                as="form"
                gap="md"
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
                <Fields>
                    <Field label="Name" grow>
                        <TextBox
                            placeholder="Polder Capital"
                            value={name}
                            onChange={(e) => setName(e.currentTarget.value)}
                        />
                    </Field>
                    <Field label="What it does" grow>
                        <TextBox
                            placeholder="trades AI questions"
                            value={mandate}
                            onChange={(e) => setMandate(e.currentTarget.value)}
                        />
                    </Field>
                </Fields>
                <Fields>
                    <Field label="Kind">
                        <ComboBox
                            value={kind}
                            onChange={(e) => setKind(e.currentTarget.value)}
                            options={[
                                { value: 'trading', label: 'trading' },
                                { value: 'cdu', label: 'keeps the CDU' },
                            ]}
                        />
                    </Field>
                </Fields>
                <Fields hidden={kind === 'cdu'}>
                    <Field label="Market">
                        <ComboBox
                            value={market}
                            onChange={(e) => setMarket(e.currentTarget.value)}
                            options={[
                                { value: 'predictions', label: 'predictions' },
                                { value: 'crypto', label: 'crypto' },
                                { value: 'stocks', label: 'stocks' },
                            ]}
                        />
                    </Field>
                    <Field label="Focus" grow>
                        <TextBox
                            placeholder="AI · BTC, ETH · semiconductors"
                            value={focus}
                            onChange={(e) => setFocus(e.currentTarget.value)}
                        />
                    </Field>
                    <Field label="Capital">
                        <TextBox
                            className="w-[96px]"
                            value={capital}
                            onChange={(e) => setCapital(e.currentTarget.value)}
                        />
                    </Field>
                </Fields>
                <Actions>
                    <CommandButton
                        type="submit"
                        isDefault
                        disabled={
                            !name.trim() || !mandate.trim() || found.isPending
                        }
                    >
                        Start
                    </CommandButton>
                </Actions>
                <Refusal error={found.error} />
            </Stack>
        </Card>
    );
}

function CduPage({ company }: { company: CompanySummary }) {
    return (
        <>
            <PageHead icon="cdu" title={company.name} sub={company.mandate} />
            <Page>
                <Staff company={company} />
            </Page>
        </>
    );
}

export function CompanyPage({ company }: { company: CompanySummary }) {
    if (company.kind === 'cdu') return <CduPage company={company} />;
    const change = bookReturn(company);
    const market = company.focus
        ? `${company.market}: ${company.focus}`
        : company.market;
    return (
        <>
            <PageHead
                icon="company"
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
            <Page>
                <Stats>
                    <Stat label="Book" value={credits(company.value)} />
                    <Stat label="Return" value={<Signed fraction={change} />} />
                    <Stat label="Cash" value={credits(company.cash)} />
                    <Stat label="Brier" value={brierLabel(company.brier)} />
                </Stats>
                <Staff company={company} />
                <Holdings company={company} />
                <Watching company={company} />
                {company.staff.length > 0 ? (
                    <TradeForm company={company} />
                ) : null}
                {company.trades.length > 0 ? (
                    <Card title="Trades">
                        <ListView
                            label="Trades"
                            selectionMode="none"
                            columns={[
                                {
                                    key: 'when',
                                    header: 'When',
                                    width: 'max-content',
                                },
                                {
                                    key: 'trade',
                                    header: 'Trade',
                                    width: 'minmax(160px, 1fr)',
                                    wrap: true,
                                },
                            ]}
                            items={company.trades.map((t) => ({
                                id: t.id,
                                label: whenLabel(t.at),
                                cells: {
                                    trade: (
                                        <span title={t.thesis ?? undefined}>
                                            {t.traderName
                                                ? `${t.traderName}: `
                                                : ''}
                                            {tradeLabel(t)} · {t.title}
                                        </span>
                                    ),
                                },
                            }))}
                        />
                    </Card>
                ) : null}
            </Page>
        </>
    );
}

const moneyColumns = (value: string, change: string) => [
    { key: 'what', header: 'What', width: 'minmax(160px, 2fr)', wrap: true },
    { key: 'value', header: value, align: 'end' as const },
    { key: 'change', header: change, align: 'end' as const },
];

function Holdings({ company }: { company: CompanySummary }) {
    return (
        <Card title="Holding">
            {company.positions.length === 0 ? (
                <Empty>Nothing held.</Empty>
            ) : (
                <ListView
                    label="Holding"
                    selectionMode="none"
                    columns={moneyColumns('Worth', 'Return')}
                    items={company.positions.map((p) => ({
                        id: p.instrumentId,
                        label: `${p.title}${p.fresh ? '' : ' (stale)'}`,
                        cells: {
                            value: credits(p.value),
                            change: <Signed fraction={positionReturn(p)} />,
                        },
                    }))}
                />
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
                <ListView
                    label="Watching"
                    selectionMode="none"
                    columns={moneyColumns('Price', 'Today')}
                    items={open.map((w) => {
                        const move = dayMove(w);
                        return {
                            id: w.instrumentId,
                            label: w.title,
                            cells: {
                                value: priceLabel(midOf(w)),
                                change:
                                    move === null ? (
                                        ''
                                    ) : (
                                        <Signed fraction={move} />
                                    ),
                            },
                        };
                    })}
                />
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
                <Rows>
                    {company.staff.map((s) => (
                        <Row key={s.participantId}>
                            <Jump
                                onClick={() =>
                                    go({ page: 'person', id: s.participantId })
                                }
                            >
                                {s.name}
                            </Jump>
                            <Stack.Item grow>
                                Brier {brierLabel(s.brier)}
                            </Stack.Item>
                            <CommandButton
                                size="small"
                                tone="caution"
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
                            </CommandButton>
                        </Row>
                    ))}
                </Rows>
            )}
            <Stack
                as="form"
                direction="row"
                align="center"
                gap="sm"
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
                <ComboBox
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
                </ComboBox>
                <CommandButton
                    type="submit"
                    isDefault
                    disabled={!who || hire.isPending}
                >
                    Hire
                </CommandButton>
            </Stack>
            <Refusal error={hire.error ?? dismiss.error} />
        </Card>
    );
}

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
            <Stack
                as="form"
                gap="md"
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
                <Fields>
                    <Field label="As">
                        <ComboBox
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
                        </ComboBox>
                    </Field>
                    <Field label="Side">
                        <ComboBox
                            value={side}
                            onChange={(e) => setSide(e.currentTarget.value)}
                            options={[
                                { value: 'buy', label: 'buy' },
                                { value: 'sell', label: 'sell' },
                            ]}
                        />
                    </Field>
                    <Field label="What" grow>
                        <ComboBox
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
                        </ComboBox>
                    </Field>
                </Fields>
                <Fields>
                    <Field label="Percent">
                        <TextBox
                            className="w-[56px]"
                            value={percent}
                            onChange={(e) => setPercent(e.currentTarget.value)}
                        />
                    </Field>
                    {company.market === 'predictions' ? (
                        <Field label="Chance %">
                            <TextBox
                                className="w-[56px]"
                                value={chance}
                                onChange={(e) =>
                                    setChance(e.currentTarget.value)
                                }
                            />
                        </Field>
                    ) : null}
                    <Field label="Why" grow>
                        <TextBox
                            value={thesis}
                            onChange={(e) => setThesis(e.currentTarget.value)}
                        />
                    </Field>
                </Fields>
                <Actions>
                    <CommandButton
                        type="submit"
                        isDefault
                        disabled={!trader || !instrument || trade.isPending}
                    >
                        Trade
                    </CommandButton>
                </Actions>
                <Refusal error={trade.error} />
            </Stack>
        </Card>
    );
}
