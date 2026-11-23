import {
    ComboBox,
    CommandButton,
    ContactRow,
    Expander,
    GroupBox,
    Hyperlink,
    Icon,
    type IconName,
    ListView,
    type ListViewColumn,
    ProgressBar,
    StatusBar,
    StatusBarField,
    SunkenField,
    Text,
    TextBox,
} from '@partytown/ui';
import { keepPreviousData, useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useState } from 'react';
import {
    getGetWorldsPartyIdQueryKey,
    useGetLlmConfigUsage,
    useGetPersona,
    useGetWorldsPartyIdActivities,
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdMessenger,
    useGetWorldsPartyIdSelves,
    useGetWorldsPartyIdSuspense,
    usePostWorldsPartyIdHappenings,
    usePostWorldsPartyIdTick,
    usePutWorldsPartyId,
} from '#api/party-zone';
import { useDesktopContext } from '#lib/desktop-context';
import type { LifeSummary } from '../../api/model/lifeSummary';
import type { SelfSummary } from '../../api/model/selfSummary';
import type { WorldActivity } from '../../api/model/worldActivity';
import type { WorldStatus } from '../../api/model/worldStatus';
import Portrait from '../../components/Portrait';
import {
    useOpenDirectChat,
    usePlayerId,
    usePlayerSession,
    WORLD_ID,
} from '../../lib/world';
import { canMoveIn, useResidency } from '../personas/residence';

type Meta = { icon: IconName; label: string };

const APPS: Record<string, Meta> = {
    rooms: { icon: 'chat-rooms', label: 'Rooms' },
    messenger: { icon: 'messenger', label: 'Messenger' },
    board: { icon: 'board', label: 'Board' },
    diary: { icon: 'diary', label: 'Diary' },
    calendar: { icon: 'calendar', label: 'Calendar' },
    life: { icon: 'life', label: 'Life' },
    memory: { icon: 'memory-graph', label: 'Memory' },
    world: { icon: 'megaphone', label: 'World' },
};

const ACTIONS: Record<string, Meta> = {
    rest: { icon: 'sleep', label: 'Rest' },
    offstage: { icon: 'film', label: 'Off-screen' },
    lived: { icon: 'home', label: 'Own life' },
};

const appMeta = (a: WorldActivity): Meta =>
    ACTIONS[a.action ?? '']
        ? ACTIONS[a.action ?? '']
        : a.action === 'error'
          ? {
                icon: 'warning',
                label: APPS[a.app ?? '']?.label ?? a.app ?? '?',
            }
          : (APPS[a.app ?? ''] ?? { icon: 'unknown', label: a.app ?? '?' });

function useNow(ms = 15000) {
    const [now, setNow] = useState(() => Date.now());
    useEffect(() => {
        const id = window.setInterval(() => setNow(Date.now()), ms);
        return () => window.clearInterval(id);
    }, [ms]);
    return now;
}

function relTime(ts: number | string | null | undefined, now: number) {
    if (ts == null) return 'never';
    const s = Math.max(0, (now - Number(ts)) / 1000);
    if (s < 10) return 'just now';
    if (s < 60) return `${Math.floor(s)}s ago`;
    if (s < 3600) return `${Math.floor(s / 60)}m ago`;
    if (s < 86400) return `${Math.floor(s / 3600)}h ago`;
    return new Date(Number(ts)).toLocaleDateString();
}

export default function WorldApp() {
    const statusQuery = useGetWorldsPartyIdSuspense(WORLD_ID);
    const livesQuery = useGetWorldsPartyIdLivesSuspense(WORLD_ID);
    const status = statusQuery.data.data;
    const allLives = livesQuery.data.data ?? [];
    const lives = allLives.filter((l) => l.driver !== 'User');
    const [filterId, setFilterId] = useState<string | null>(null);
    const now = useNow();
    const selvesQuery = useGetWorldsPartyIdSelves(WORLD_ID, {
        query: { refetchInterval: 60_000 },
    });
    const selfById = new Map(
        (selvesQuery.data?.data ?? []).map((s) => [s.personaId, s]),
    );

    const busyCount = lives.filter((l) => l.busy).length;
    const nameById = new Map(lives.map((l) => [l.personaId, l.name]));

    return (
        <div className="flex h-full flex-col">
            <ClockBar status={status} />
            <HappeningBar />
            <div className="flex min-h-0 flex-1 gap-2 px-2 pb-2">
                <GroupBox
                    title={`Lives (${lives.length})`}
                    icon={<Icon name="life" />}
                    className="flex w-[300px] min-h-0 flex-none flex-col gap-2"
                >
                    <InviteBar residentIds={allLives.map((l) => l.personaId)} />
                    <SunkenField
                        role="group"
                        aria-label="Lives"
                        className="flex flex-1 flex-col gap-1"
                    >
                        {lives.length === 0 ? (
                            <Text as="p" tone="muted" className="px-3 py-2">
                                No AI personas in this world yet. Invite one
                                above.
                            </Text>
                        ) : null}
                        {lives.map((life) => (
                            <LifeRow
                                key={life.personaId}
                                life={life}
                                self={selfById.get(life.personaId)}
                                busy={life.busy}
                                selected={filterId === life.personaId}
                                onSelect={() =>
                                    setFilterId((cur) =>
                                        cur === life.personaId
                                            ? null
                                            : life.personaId,
                                    )
                                }
                                now={now}
                            />
                        ))}
                    </SunkenField>
                </GroupBox>
                <ActivityFeed
                    filterId={filterId}
                    filterName={
                        lives.find((l) => l.personaId === filterId)?.name
                    }
                    onClearFilter={() => setFilterId(null)}
                    nameById={nameById}
                    now={now}
                />
            </div>
            <StatusBar>
                <StatusBarField
                    icon={<Icon name={status.running ? 'play' : 'stop'} />}
                >
                    {status.running ? 'Running' : 'Paused'}
                </StatusBarField>
                <StatusBarField>
                    Tick every {Number(status.tickSeconds ?? 0)}s
                </StatusBarField>
                <StatusBarField>
                    {Number(status.actionsLastHour ?? 0)} /{' '}
                    {Number(status.maxActionsPerHour ?? 0)} actions this hour
                </StatusBarField>
                <StatusBarField>
                    {busyCount} of {lives.length} busy
                </StatusBarField>
                <StatusBarField>
                    Last tick: {relTime(status.lastTickAt, now)}
                </StatusBarField>
                <SpendCell />
            </StatusBar>
        </div>
    );
}

const usd = (n: number) => `$${n < 1 ? n.toFixed(3) : n.toFixed(2)}`;

function SpendCell() {
    const signedIn = usePlayerSession((s) => !!s.userId);
    const usageQuery = useGetLlmConfigUsage({
        query: {
            enabled: signedIn,
            refetchInterval: 30_000,
            staleTime: 15_000,
        },
    });
    const rows = usageQuery.data?.data;
    if (!Array.isArray(rows))
        return (
            <StatusBarField grow icon={<Icon name="money" />}>
                —
            </StatusBarField>
        );
    const used = rows.reduce((sum, r) => sum + Number(r.usageUsd ?? 0), 0);
    const limits = rows.filter((r) => r.limitUsd != null);
    const limit = limits.reduce((sum, r) => sum + Number(r.limitUsd), 0);
    const detail = rows
        .map(
            (r) =>
                `${r.modelName ?? r.providerId}: ${usd(Number(r.usageUsd))}${r.limitUsd != null ? ` / ${usd(Number(r.limitUsd))}` : ''}`,
        )
        .join('\n');
    return (
        <StatusBarField
            grow
            icon={<Icon name="money" />}
            title={detail || 'No providers'}
        >
            {usd(used)}
            {limits.length ? ` / ${usd(limit)}` : ''} spent
        </StatusBarField>
    );
}

function InviteBar({ residentIds }: { residentIds: string[] }) {
    const personas = useGetPersona();
    const [pick, setPick] = useState('');
    const { moveIn, isPending } = useResidency();
    const candidates = (personas.data?.data ?? []).filter(
        (p) => !residentIds.includes(p.id ?? '') && canMoveIn(p),
    );
    if (candidates.length === 0) return null;
    return (
        <form
            className="flex items-center gap-1"
            onSubmit={(e) => {
                e.preventDefault();
                if (pick) moveIn(pick, () => setPick(''));
            }}
        >
            <ComboBox
                value={pick}
                onChange={(e) => setPick(e.currentTarget.value)}
                className="min-w-0 flex-1"
                aria-label="Persona to invite"
                options={[
                    { value: '', label: 'Invite a persona…' },
                    ...candidates.map((p) => ({
                        value: p.id ?? '',
                        label: p.name,
                    })),
                ]}
            />
            <CommandButton type="submit" disabled={!pick || isPending}>
                Invite
            </CommandButton>
        </form>
    );
}

function HappeningBar() {
    const inputId = useId();
    const [text, setText] = useState('');
    const [note, setNote] = useState<string | null>(null);
    const announce = usePostWorldsPartyIdHappenings({
        mutation: {
            onSuccess: () => {
                setText('');
                setNote('Announced.');
            },
            onError: () => setNote('Could not announce.'),
        },
    });
    return (
        <form
            className="mx-2 mb-2 flex items-center gap-1.5"
            onSubmit={(e) => {
                e.preventDefault();
                const trimmed = text.trim();
                if (!trimmed) return;
                announce.mutate({ partyId: WORLD_ID, data: { text: trimmed } });
            }}
        >
            <label
                htmlFor={inputId}
                className="flex items-center gap-1 whitespace-nowrap"
                title="Every persona perceives and remembers it"
            >
                <Icon name="megaphone" />
                <Text strong>Happening:</Text>
            </label>
            <TextBox
                id={inputId}
                maxLength={600}
                placeholder="Announce something that happens in the world"
                value={text}
                onChange={(e) => {
                    setText(e.currentTarget.value);
                    setNote(null);
                }}
                className="min-w-0 flex-1"
            />
            {note ? (
                <Text tone="muted" className="whitespace-nowrap">
                    {note}
                </Text>
            ) : null}
            <CommandButton
                type="submit"
                disabled={announce.isPending || !text.trim()}
            >
                {announce.isPending ? 'Announcing…' : 'Announce'}
            </CommandButton>
        </form>
    );
}

function NumberField({
    label,
    unit,
    title,
    min,
    value,
    onChange,
    onCommit,
}: {
    label: string;
    unit: string;
    title?: string;
    min: number;
    value: string;
    onChange: (value: string) => void;
    onCommit: () => void;
}) {
    const id = useId();
    return (
        <span className="flex items-center gap-1" title={title}>
            <label htmlFor={id}>{label}</label>
            <TextBox
                id={id}
                type="number"
                min={min}
                value={value}
                className="w-[56px]"
                onChange={(e) => onChange(e.currentTarget.value)}
                onBlur={onCommit}
                onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
            />
            {unit}
        </span>
    );
}

function ClockBar({ status }: { status: WorldStatus }) {
    const queryClient = useQueryClient();
    const [tickDraft, setTickDraft] = useState(String(status.tickSeconds));
    const [budgetDraft, setBudgetDraft] = useState(
        String(status.maxActionsPerHour),
    );
    const [tickNote, setTickNote] = useState<string | null>(null);
    const [ambientDraft, setAmbientDraft] = useState(
        String(status.ambientMinutes ?? 0),
    );
    useEffect(
        () => setAmbientDraft(String(status.ambientMinutes ?? 0)),
        [status.ambientMinutes],
    );

    useEffect(
        () => setTickDraft(String(status.tickSeconds)),
        [status.tickSeconds],
    );
    useEffect(
        () => setBudgetDraft(String(status.maxActionsPerHour)),
        [status.maxActionsPerHour],
    );

    const update = usePutWorldsPartyId({
        mutation: {
            onSuccess: (res) =>
                queryClient.setQueryData(
                    getGetWorldsPartyIdQueryKey(WORLD_ID),
                    res,
                ),
        },
    });
    const tick = usePostWorldsPartyIdTick({
        mutation: {
            onSuccess: (res) =>
                setTickNote(
                    res.data.started
                        ? 'Someone is thinking…'
                        : 'Nobody free to act right now.',
                ),
            onError: () => setTickNote('Tick failed.'),
        },
    });

    const put = (data: {
        running?: boolean;
        tickSeconds?: number;
        maxActionsPerHour?: number;
        ambientMinutes?: number;
    }) =>
        update.mutate({
            partyId: WORLD_ID,
            data: {
                running: data.running ?? null,
                tickSeconds: data.tickSeconds ?? null,
                maxActionsPerHour: data.maxActionsPerHour ?? null,
                ambientMinutes: data.ambientMinutes ?? null,
            },
        });

    const commitNumber = (
        draft: string,
        current: number | string | undefined,
        key: 'tickSeconds' | 'maxActionsPerHour' | 'ambientMinutes',
        reset: (v: string) => void,
    ) => {
        const n = Math.round(Number(draft));
        const min = key === 'ambientMinutes' ? 0 : 1;
        if (!Number.isFinite(n) || n < min) return reset(String(current));
        if (n !== Number(current)) put({ [key]: n });
    };

    const used = Number(status.actionsLastHour ?? 0);
    const budget = Math.max(1, Number(status.maxActionsPerHour ?? 1));

    return (
        <GroupBox
            title="World Clock"
            icon={<Icon name="clock" />}
            className="m-2 mb-1"
        >
            <div className="flex flex-wrap items-center gap-3">
                <CommandButton
                    disabled={update.isPending}
                    tone={status.running ? 'neutral' : 'go'}
                    icon={<Icon name={status.running ? 'pause' : 'play'} />}
                    className="min-w-[88px]"
                    title={
                        status.running
                            ? 'Pause the world clock — personas stop acting on their own'
                            : 'Start the world clock — personas act on their own (costs tokens)'
                    }
                    onClick={() => put({ running: !status.running })}
                >
                    {status.running ? 'Pause' : 'Run'}
                </CommandButton>
                <NumberField
                    label="Happening every"
                    unit="min"
                    title="The Narrator lets something small happen every N minutes (0 = off)"
                    min={0}
                    value={ambientDraft}
                    onChange={setAmbientDraft}
                    onCommit={() =>
                        commitNumber(
                            ambientDraft,
                            status.ambientMinutes,
                            'ambientMinutes',
                            setAmbientDraft,
                        )
                    }
                />
                <NumberField
                    label="Tick every"
                    unit="s"
                    min={1}
                    value={tickDraft}
                    onChange={setTickDraft}
                    onCommit={() =>
                        commitNumber(
                            tickDraft,
                            status.tickSeconds,
                            'tickSeconds',
                            setTickDraft,
                        )
                    }
                />
                <NumberField
                    label="Max"
                    unit="actions/hour"
                    min={1}
                    value={budgetDraft}
                    onChange={setBudgetDraft}
                    onCommit={() =>
                        commitNumber(
                            budgetDraft,
                            status.maxActionsPerHour,
                            'maxActionsPerHour',
                            setBudgetDraft,
                        )
                    }
                />
                <div
                    className="flex items-center gap-1.5"
                    title="Actions started in the last hour vs the hourly budget"
                >
                    <ProgressBar
                        value={Math.min(used, budget)}
                        max={budget}
                        label="Actions this hour"
                        valueText={`${used} of ${budget}`}
                        tone={used >= budget ? 'danger' : 'neutral'}
                        className="w-[112px]"
                    />
                    <Text tone={used >= budget ? 'danger' : undefined}>
                        {used} / {budget}
                    </Text>
                </div>
                <div className="ml-auto flex items-center gap-1.5">
                    {tickNote ? <Text tone="muted">{tickNote}</Text> : null}
                    <CommandButton
                        disabled={tick.isPending}
                        icon={<Icon name="fast-forward" />}
                        title="Give one random idle persona a Life turn now (ignores pause and interval)"
                        onClick={() =>
                            tick.mutate({
                                partyId: WORLD_ID,
                                data: { personaId: null, nudge: null },
                            })
                        }
                    >
                        Tick now
                    </CommandButton>
                </div>
            </div>
            {update.isError ? (
                <Text as="p" tone="danger">
                    Could not update the clock.
                </Text>
            ) : null}
        </GroupBox>
    );
}

function SelfLines({ self }: { self: SelfSummary }) {
    const asleep = self.doing === 'Asleep';
    const battery = Number(self.battery ?? 1);
    const fed = self.fed == null ? null : Number(self.fed);
    const hunger =
        fed === null || fed >= 0.45 ? null : fed < 0.15 ? 'famished' : 'hungry';
    return (
        <div className="grid gap-0.5">
            <div
                className="flex min-w-0 items-center gap-1"
                title={self.doing ?? undefined}
            >
                <Icon name={asleep ? 'sleep' : 'home'} />
                <Text className="truncate">
                    {asleep ? 'asleep' : (self.doing ?? 'up and about')}
                </Text>
            </div>
            <Text as="div" tone="muted">
                {self.mood ? `mood: ${self.mood}` : null}
                {self.energy ? ` · energy ${self.energy}` : null}
                {battery < 0.5 ? ' · talked out' : null}
                {hunger ? ` · ${hunger}` : null}
                {self.ailing ? ' · unwell' : null}
            </Text>
            {self.haunts && self.haunts.length > 0 ? (
                <Text as="div" tone="muted">
                    haunts: {self.haunts.join(', ')}
                </Text>
            ) : null}
            {self.threads && self.threads.length > 0 ? (
                <Expander
                    title="Going on in their life"
                    count={self.threads.length}
                    defaultOpen={false}
                >
                    <ul className="m-0 grid gap-0.5 pl-4">
                        {self.threads.map((t) => (
                            <li key={t}>
                                <Text>{t}</Text>
                            </li>
                        ))}
                    </ul>
                </Expander>
            ) : null}
        </div>
    );
}

function LifeRow({
    life,
    self,
    busy,
    selected,
    onSelect,
    now,
}: {
    life: LifeSummary;
    self?: SelfSummary;
    busy: boolean;
    selected: boolean;
    onSelect: () => void;
    now: number;
}) {
    const { openWindow, setWindowProps } = useDesktopContext();
    const [nudging, setNudging] = useState(false);
    const [nudge, setNudge] = useState('');
    const [note, setNote] = useState<string | null>(null);
    const tick = usePostWorldsPartyIdTick({
        mutation: {
            onSuccess: (res) => {
                setNote(res.data.started ? null : 'Busy — try again soon.');
                if (res.data.started) {
                    setNudging(false);
                    setNudge('');
                }
            },
            onError: () => setNote('Nudge failed.'),
        },
    });
    const chattiness = Number(life.chattiness ?? 0);
    const last = life.lastActivity;

    const sendNudge = () =>
        tick.mutate({
            partyId: WORLD_ID,
            data: { personaId: life.personaId, nudge: nudge.trim() || null },
        });

    const openDiary = () => {
        openWindow('diary', { props: { personaId: life.personaId } });
        setWindowProps('diary', (p) => ({ ...p, personaId: life.personaId }));
    };

    return (
        <div className="grid gap-1 px-1 py-1">
            <div className="flex items-center gap-1">
                <ContactRow
                    className="min-w-0 flex-1"
                    selected={selected}
                    aria-pressed={selected}
                    title={
                        selected
                            ? 'Show everyone in the activity feed'
                            : `Show only ${life.name} in the activity feed`
                    }
                    onClick={onSelect}
                    picture={<Portrait who={life.personaId} size={24} />}
                    name={life.name}
                    details={
                        <span
                            className="flex items-center gap-1"
                            title={`Chattiness ${chattiness.toFixed(2)}`}
                        >
                            chatty
                            <ProgressBar
                                value={Math.round(chattiness * 100)}
                                max={100}
                                tone="neutral"
                                label={`${life.name}'s chattiness`}
                                className="w-[48px]"
                            />
                            {busy ? (
                                <span
                                    className="flex items-center gap-1"
                                    title="Taking a Life turn right now"
                                >
                                    <Icon name="hourglass" />
                                    <Text tone="caution">thinking</Text>
                                </span>
                            ) : null}
                        </span>
                    }
                />
                <CommandButton
                    size="small"
                    title="Give this persona a Life turn now, optionally with a hint"
                    onClick={() => setNudging((v) => !v)}
                >
                    Nudge
                </CommandButton>
                <CommandButton
                    size="small"
                    title="Read this persona's diary"
                    onClick={openDiary}
                >
                    Diary
                </CommandButton>
            </div>
            <div className="grid gap-0.5 pl-3">
                {self ? <SelfLines self={self} /> : null}
                {last ? (
                    <div
                        className="flex min-w-0 items-center gap-1"
                        title={last.summary}
                    >
                        <Icon name={appMeta(last).icon} />
                        <Text className="truncate">
                            {last.summary}{' '}
                            <Text tone="muted">· {relTime(last.ts, now)}</Text>
                        </Text>
                    </div>
                ) : (
                    <Text as="div" tone="muted">
                        Hasn't done anything yet.
                    </Text>
                )}
                {nudging ? (
                    <form
                        className="flex gap-1"
                        onSubmit={(e) => {
                            e.preventDefault();
                            sendNudge();
                        }}
                    >
                        <TextBox
                            autoFocus
                            placeholder="Optional hint, e.g. check the Board"
                            aria-label={`Hint for ${life.name}`}
                            value={nudge}
                            onChange={(e) => setNudge(e.currentTarget.value)}
                            onKeyDown={(e) =>
                                e.key === 'Escape' && setNudging(false)
                            }
                            className="min-w-0 flex-1"
                        />
                        <CommandButton
                            type="submit"
                            size="small"
                            disabled={tick.isPending || busy}
                        >
                            {tick.isPending ? '…' : 'Go'}
                        </CommandButton>
                    </form>
                ) : null}
                {note ? (
                    <Text as="div" tone="caution">
                        {note}
                    </Text>
                ) : null}
            </div>
        </div>
    );
}

const columns: ListViewColumn[] = [
    { key: 'label', header: 'Time', width: '62px' },
    { key: 'persona', header: 'Persona', width: '96px' },
    { key: 'app', header: 'App', width: '104px' },
    { key: 'what', header: 'What happened', wrap: true },
];

function ActivityFeed({
    filterId,
    filterName,
    onClearFilter,
    nameById,
    now,
}: {
    filterId: string | null;
    filterName?: string;
    nameById: Map<string, string>;
    onClearFilter: () => void;
    now: number;
}) {
    const { openWindow, setWindowProps } = useDesktopContext();
    const openDirectChat = useOpenDirectChat();
    const activitiesQuery = useGetWorldsPartyIdActivities(
        WORLD_ID,
        { limit: 100, ...(filterId ? { personaId: filterId } : {}) },
        { query: { placeholderData: keepPreviousData } },
    );
    const playerId = usePlayerId();
    const dmsQuery = useGetWorldsPartyIdMessenger(
        WORLD_ID,
        { personaId: playerId ?? undefined },
        { query: { enabled: !!playerId } },
    );
    const activities = activitiesQuery.data?.data ?? [];
    const dms = dmsQuery.data?.data ?? [];

    const openFor = (a: WorldActivity): (() => void) | null => {
        const target = a.targetId;
        if (!target) return null;
        if (a.app === 'board') {
            return () => {
                openWindow('board', { props: { threadId: target } });
                setWindowProps('board', (p) => ({ ...p, threadId: target }));
            };
        }
        if (a.app === 'messenger' || a.app === 'memory') {
            const dm = dms.find((d) => d.chatGroupId === target);
            if (dm) return () => openDirectChat(dm.chatGroupId, dm.withName);
            if (a.app !== 'messenger' || !a.withPersonaId) return null;
            const pair = `${a.personaName} & ${nameById.get(a.withPersonaId) ?? 'someone'}`;
            return () =>
                openWindow('messenger-chat', {
                    instanceId: `dm-${target}`,
                    title: `${pair} — Conversation`,
                    props: {
                        chatGroupId: target,
                        withName: pair,
                        spectate: true,
                    },
                });
        }
        return null;
    };

    const keyOf = (a: WorldActivity, index: number) => a.id ?? `row-${index}`;
    const opens = new Map(
        activities.map((a, index) => [keyOf(a, index), openFor(a)]),
    );

    const items = activities.map((a, index) => {
        const meta = appMeta(a);
        const id = keyOf(a, index);
        const open = opens.get(id);
        return {
            id,
            tone: a.action === 'error' ? ('danger' as const) : undefined,
            label: (
                <span title={relTime(a.ts, now)}>
                    {new Date(Number(a.ts)).toLocaleTimeString([], {
                        hour: '2-digit',
                        minute: '2-digit',
                    })}
                </span>
            ),
            cells: {
                persona: (
                    <Text strong className="truncate">
                        {a.personaName ||
                            (a.personaId && nameById.get(a.personaId)) ||
                            '—'}
                    </Text>
                ),
                app: (
                    <span className="flex items-center gap-1" title={a.action}>
                        <Icon name={meta.icon} />
                        {meta.label}
                    </span>
                ),
                what: (
                    <div className="grid gap-0.5">
                        <div>
                            {open ? (
                                <Hyperlink onClick={open}>
                                    {a.summary}
                                </Hyperlink>
                            ) : (
                                a.summary
                            )}
                        </div>
                        {a.thought ? (
                            <Text as="div" tone="muted" italic>
                                “{a.thought}”
                            </Text>
                        ) : null}
                    </div>
                ),
            },
        };
    });

    return (
        <GroupBox
            title={`Activity${filterName ? ` — ${filterName}` : ''}`}
            icon={<Icon name="world" />}
            className="flex min-h-0 min-w-0 flex-1 flex-col gap-2"
        >
            {filterId ? (
                <div className="flex">
                    <CommandButton
                        size="small"
                        icon={<Icon name="close" />}
                        onClick={onClearFilter}
                    >
                        Show everyone
                    </CommandButton>
                </div>
            ) : null}
            {activitiesQuery.isError ? (
                <Text as="p" tone="danger">
                    Could not load activity.
                </Text>
            ) : null}
            <ListView
                view="details"
                label="Activity"
                selectionMode="none"
                columns={columns}
                items={items}
                className="min-h-0 flex-1"
                onActivate={(id) => opens.get(id)?.()}
                empty={
                    activitiesQuery.isPending
                        ? undefined
                        : 'Nothing has happened yet. Press Run or Tick now to wake someone up.'
                }
            />
        </GroupBox>
    );
}
