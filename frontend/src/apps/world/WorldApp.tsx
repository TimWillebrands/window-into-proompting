import { keepPreviousData, useQueryClient } from '@tanstack/react-query';
import {
    type CSSProperties,
    type ReactNode,
    useEffect,
    useId,
    useState,
} from 'react';
import {
    getGetWorldsPartyIdLivesQueryKey,
    getGetWorldsPartyIdQueryKey,
    useGetLlmConfigUsage,
    useGetPersona,
    useGetWorldsPartyIdActivities,
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdMessenger,
    useGetWorldsPartyIdSuspense,
    usePostWorldsPartyIdHappenings,
    usePostWorldsPartyIdResidents,
    usePostWorldsPartyIdTick,
    usePutWorldsPartyId,
} from '#api/party-zone';
import { useDesktopContext } from '#lib/desktop-context';
import type { LifeSummary } from '../../api/model/lifeSummary';
import type { WorldActivity } from '../../api/model/worldActivity';
import type { WorldStatus } from '../../api/model/worldStatus';
import { useOpenDirectChat, usePlayerId, WORLD_ID } from '../../lib/world';

const NARRATOR_ID = '0000aaaa-0000-0000-0000-00000000a17e';

const APPS: Record<string, { icon: string; label: string }> = {
    rooms: { icon: '💬', label: 'Rooms' },
    messenger: { icon: '🦋', label: 'Messenger' },
    board: { icon: '📌', label: 'Board' },
    diary: { icon: '📓', label: 'Diary' },
    life: { icon: '🌱', label: 'Life' },
    memory: { icon: '🧠', label: 'Memory' },
    world: { icon: '📣', label: 'World' },
};

const appMeta = (a: WorldActivity) =>
    a.action === 'rest'
        ? { icon: '💤', label: 'Rest' }
        : a.action === 'error'
          ? { icon: '⚠️', label: APPS[a.app ?? '']?.label ?? a.app ?? '?' }
          : (APPS[a.app ?? ''] ?? { icon: '❔', label: a.app ?? '?' });

/** Re-renders on an interval so relative times stay fresh. */
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

const sectionHeader =
    'bg-gradient-to-b from-xp-section-from to-xp-section-to text-white text-[11px] font-semibold px-2 py-[3px] flex items-center justify-between shadow-[inset_0_1px_0_rgba(255,255,255,0.35)]';

/** World Monitor — Task Manager for the World clock, Lives and the Activity feed. */
export default function WorldApp() {
    const statusQuery = useGetWorldsPartyIdSuspense(WORLD_ID);
    const livesQuery = useGetWorldsPartyIdLivesSuspense(WORLD_ID);
    const status = statusQuery.data.data;
    const allLives = livesQuery.data.data ?? [];
    const lives = allLives.filter((l) => l.driver !== 'User');
    const [filterId, setFilterId] = useState<string | null>(null);
    const now = useNow();

    // Lives refresh on activity events too; status only on clock.
    const busyCount = lives.filter((l) => l.busy).length;
    const nameById = new Map(lives.map((l) => [l.personaId, l.name]));

    return (
        <div
            className="app-surface flex flex-col h-full"
            style={{ background: 'transparent' }}
        >
            <ClockBar status={status} />
            <HappeningBar />
            <div className="flex flex-1 min-h-0">
                <div
                    className="xp-glass-panel flex flex-col"
                    style={{
                        width: 300,
                        borderRight: '1px solid rgba(255,255,255,0.7)',
                    }}
                >
                    <div className={sectionHeader}>
                        <span>Lives</span>
                        <span
                            className="xp-glass-chip"
                            style={{ color: '#003399', fontWeight: 700 }}
                        >
                            {lives.length}
                        </span>
                    </div>
                    <InviteBar residentIds={allLives.map((l) => l.personaId)} />
                    <div className="flex-1 overflow-y-auto py-1">
                        {lives.length === 0 ? (
                            <p style={{ color: '#666', padding: '8px 12px' }}>
                                No AI personas in this world yet. Invite one
                                above.
                            </p>
                        ) : null}
                        {lives.map((life) => (
                            <LifeRow
                                key={life.personaId}
                                life={life}
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
                    </div>
                </div>
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
                <span>{status.running ? '▶ Running' : '■ Paused'}</span>
                <span>Tick every {Number(status.tickSeconds ?? 0)}s</span>
                <span>
                    {Number(status.actionsLastHour ?? 0)} /{' '}
                    {Number(status.maxActionsPerHour ?? 0)} actions this hour
                </span>
                <span>
                    {busyCount} of {lives.length} busy
                </span>
                <span>Last tick: {relTime(status.lastTickAt, now)}</span>
                <SpendCell />
            </StatusBar>
        </div>
    );
}

function StatusBar({ children }: { children: ReactNode[] }) {
    return (
        <div
            className="flex"
            style={{
                gap: 2,
                padding: 2,
                background: 'var(--color-xp-face)',
                borderTop: '1px solid #fff',
                fontSize: 11,
            }}
        >
            {children.map((child, i) => (
                <div
                    // biome-ignore lint/suspicious/noArrayIndexKey: static cells
                    key={i}
                    style={{
                        padding: '1px 6px',
                        border: '1px solid',
                        borderColor: '#aca899 #fff #fff #aca899',
                        whiteSpace: 'nowrap',
                        flex: i === children.length - 1 ? 1 : undefined,
                    }}
                >
                    {child}
                </div>
            ))}
        </div>
    );
}

const usd = (n: number) => `$${n < 1 ? n.toFixed(3) : n.toFixed(2)}`;

/** LLM spend across providers, polled. */
function SpendCell() {
    const usageQuery = useGetLlmConfigUsage({
        query: { refetchInterval: 30_000, staleTime: 15_000 },
    });
    const rows = usageQuery.data?.data;
    if (!Array.isArray(rows)) return <span>💲 —</span>;
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
        <span title={detail || 'No providers'}>
            💲 {usd(used)}
            {limits.length ? ` / ${usd(limit)}` : ''} spent
        </span>
    );
}

/** Author-announced Happening: everyone perceives and remembers it. */
/** Invite a library Persona to move into the World. */
function InviteBar({ residentIds }: { residentIds: string[] }) {
    const queryClient = useQueryClient();
    const personas = useGetPersona();
    const [pick, setPick] = useState('');
    const invite = usePostWorldsPartyIdResidents({
        mutation: {
            onSuccess: () => {
                setPick('');
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdLivesQueryKey(WORLD_ID),
                });
            },
        },
    });
    const candidates = (personas.data?.data ?? []).filter(
        (p) =>
            !residentIds.includes(p.id ?? '') &&
            p.id !== NARRATOR_ID &&
            p.systemPrompt !== 'A human visiting this world.',
    );
    if (candidates.length === 0) return null;
    return (
        <form
            className="flex items-center"
            style={{ gap: 4, padding: '4px 8px' }}
            onSubmit={(e) => {
                e.preventDefault();
                if (pick)
                    invite.mutate({
                        partyId: WORLD_ID,
                        data: { personaId: pick },
                    });
            }}
        >
            <select
                value={pick}
                onChange={(e) => setPick(e.currentTarget.value)}
                style={{ flex: 1, minWidth: 0 }}
                aria-label="Persona to invite"
            >
                <option value="">Invite a persona…</option>
                {candidates.map((p) => (
                    <option key={p.id} value={p.id ?? ''}>
                        {p.name}
                    </option>
                ))}
            </select>
            <button type="submit" disabled={!pick || invite.isPending}>
                Invite
            </button>
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
            className="flex items-center"
            style={{ gap: 6, margin: '0 8px 6px' }}
            onSubmit={(e) => {
                e.preventDefault();
                const trimmed = text.trim();
                if (!trimmed) return;
                announce.mutate({ partyId: WORLD_ID, data: { text: trimmed } });
            }}
        >
            <label
                htmlFor={inputId}
                style={{ fontWeight: 600, whiteSpace: 'nowrap' }}
                title="Every persona perceives and remembers it"
            >
                📣 Happening:
            </label>
            <input
                id={inputId}
                type="text"
                maxLength={600}
                placeholder="Announce something that happens in the world"
                value={text}
                onChange={(e) => {
                    setText(e.currentTarget.value);
                    setNote(null);
                }}
                style={{ flex: 1, minWidth: 0 }}
            />
            {note ? (
                <span style={{ color: '#666', whiteSpace: 'nowrap' }}>
                    {note}
                </span>
            ) : null}
            <button type="submit" disabled={announce.isPending || !text.trim()}>
                {announce.isPending ? 'Announcing…' : 'Announce'}
            </button>
        </form>
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
    const pct = Math.min(100, (used / budget) * 100);

    return (
        <fieldset
            className="border border-[#9db2c8] m-2 mb-1"
            style={{ padding: '4px 8px 6px' }}
        >
            <legend className="text-xp-legend font-semibold text-[11px] px-1">
                World Clock
            </legend>
            <div className="flex items-center flex-wrap" style={{ gap: 12 }}>
                <button
                    type="button"
                    disabled={update.isPending}
                    style={{ minWidth: 86, fontWeight: 700 }}
                    title={
                        status.running
                            ? 'Pause the world clock — personas stop acting on their own'
                            : 'Start the world clock — personas act on their own (costs tokens)'
                    }
                    onClick={() => put({ running: !status.running })}
                >
                    {status.running ? '❚❚ Pause' : '▶ Run'}
                </button>
                <label
                    className="flex items-center"
                    style={{ gap: 4 }}
                    title="The Narrator lets something small happen every N minutes (0 = off)"
                >
                    Happening every
                    <input
                        type="number"
                        min={0}
                        value={ambientDraft}
                        style={{ width: 46 }}
                        onChange={(e) => setAmbientDraft(e.currentTarget.value)}
                        onBlur={() =>
                            commitNumber(
                                ambientDraft,
                                status.ambientMinutes,
                                'ambientMinutes',
                                setAmbientDraft,
                            )
                        }
                        onKeyDown={(e) =>
                            e.key === 'Enter' && e.currentTarget.blur()
                        }
                    />
                    min
                </label>
                <label className="flex items-center" style={{ gap: 4 }}>
                    Tick every
                    <input
                        type="number"
                        min={1}
                        value={tickDraft}
                        style={{ width: 52 }}
                        onChange={(e) => setTickDraft(e.currentTarget.value)}
                        onBlur={() =>
                            commitNumber(
                                tickDraft,
                                status.tickSeconds,
                                'tickSeconds',
                                setTickDraft,
                            )
                        }
                        onKeyDown={(e) =>
                            e.key === 'Enter' && e.currentTarget.blur()
                        }
                    />
                    s
                </label>
                <label className="flex items-center" style={{ gap: 4 }}>
                    Max
                    <input
                        type="number"
                        min={1}
                        value={budgetDraft}
                        style={{ width: 52 }}
                        onChange={(e) => setBudgetDraft(e.currentTarget.value)}
                        onBlur={() =>
                            commitNumber(
                                budgetDraft,
                                status.maxActionsPerHour,
                                'maxActionsPerHour',
                                setBudgetDraft,
                            )
                        }
                        onKeyDown={(e) =>
                            e.key === 'Enter' && e.currentTarget.blur()
                        }
                    />
                    actions/hour
                </label>
                <div
                    className="flex items-center"
                    style={{ gap: 6 }}
                    title="Actions started in the last hour vs the hourly budget"
                >
                    <div
                        style={{
                            width: 110,
                            height: 12,
                            border: '1px solid',
                            borderColor: '#7f9db9',
                            background: '#fff',
                            padding: 1,
                        }}
                    >
                        <div
                            style={{
                                width: `${pct}%`,
                                height: '100%',
                                background:
                                    pct >= 100
                                        ? 'linear-gradient(180deg,#f08272,#a13322)'
                                        : 'linear-gradient(180deg,#8fe08a,#2a7d23)',
                            }}
                        />
                    </div>
                    <span>
                        {used} / {budget}
                    </span>
                </div>
                <div
                    className="flex items-center"
                    style={{ gap: 6, marginLeft: 'auto' }}
                >
                    {tickNote ? (
                        <span style={{ color: '#666' }}>{tickNote}</span>
                    ) : null}
                    <button
                        type="button"
                        disabled={tick.isPending}
                        title="Give one random idle persona a Life turn now (ignores pause and interval)"
                        onClick={() =>
                            tick.mutate({
                                partyId: WORLD_ID,
                                data: { personaId: null, nudge: null },
                            })
                        }
                    >
                        ⏩ Tick now
                    </button>
                </div>
            </div>
            {update.isError ? (
                <p style={{ color: '#c00', margin: '4px 0 0' }}>
                    Could not update the clock.
                </p>
            ) : null}
        </fieldset>
    );
}

function LifeRow({
    life,
    busy,
    selected,
    onSelect,
    now,
}: {
    life: LifeSummary;
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
        <div
            className={`xp-glass-row ${selected ? 'active' : ''}`}
            style={{
                flexDirection: 'column',
                alignItems: 'stretch',
                gap: 3,
                cursor: 'default',
            }}
        >
            <div className="flex items-center" style={{ gap: 8 }}>
                <button
                    type="button"
                    className="xp-bare flex items-center"
                    style={{
                        gap: 8,
                        flex: 1,
                        minWidth: 0,
                        background: 'none',
                        border: 'none',
                        padding: 0,
                        textAlign: 'left',
                        cursor: 'pointer',
                    }}
                    title={
                        selected
                            ? 'Show everyone in the activity feed'
                            : `Show only ${life.name} in the activity feed`
                    }
                    onClick={onSelect}
                >
                    <img
                        src={`https://robohash.org/${life.personaId}.png?size=24x24`}
                        alt=""
                        style={{
                            width: 24,
                            height: 24,
                            borderRadius: '50%',
                            background: '#fff',
                            boxShadow:
                                '0 0 0 1px rgba(255,255,255,0.9), 0 1px 3px rgba(31,55,148,0.25)',
                            flexShrink: 0,
                        }}
                    />
                    <div style={{ minWidth: 0, flex: 1 }}>
                        <div
                            className="flex items-center"
                            style={{ gap: 6, fontWeight: 600 }}
                        >
                            <span
                                style={{
                                    overflow: 'hidden',
                                    textOverflow: 'ellipsis',
                                    whiteSpace: 'nowrap',
                                }}
                            >
                                {life.name}
                            </span>
                            {busy ? (
                                <span
                                    className="xp-glass-chip"
                                    style={{ color: '#a15c00' }}
                                    title="Taking a Life turn right now"
                                >
                                    ⏳ thinking
                                </span>
                            ) : null}
                        </div>
                        <div
                            className="flex items-center"
                            style={{ gap: 4, color: '#666', fontSize: 10 }}
                            title={`Chattiness ${chattiness.toFixed(2)}`}
                        >
                            chatty
                            <div
                                style={{
                                    width: 50,
                                    height: 5,
                                    background: 'rgba(0,0,0,0.1)',
                                    borderRadius: 3,
                                }}
                            >
                                <div
                                    style={{
                                        width: `${chattiness * 100}%`,
                                        height: '100%',
                                        borderRadius: 3,
                                        background: '#3596fa',
                                    }}
                                />
                            </div>
                        </div>
                    </div>
                </button>
                <button
                    type="button"
                    style={{ padding: '1px 6px' }}
                    title="Give this persona a Life turn now, optionally with a hint"
                    onClick={() => setNudging((v) => !v)}
                >
                    Nudge
                </button>
                <button
                    type="button"
                    style={{ padding: '1px 6px' }}
                    title="Read this persona's diary"
                    onClick={openDiary}
                >
                    Diary
                </button>
            </div>
            {last ? (
                <div
                    style={{
                        color: '#444',
                        fontSize: 10,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                    }}
                    title={last.summary}
                >
                    {appMeta(last).icon} {last.summary}{' '}
                    <span style={{ color: '#888' }}>
                        · {relTime(last.ts, now)}
                    </span>
                </div>
            ) : (
                <div style={{ color: '#888', fontSize: 10 }}>
                    Hasn't done anything yet.
                </div>
            )}
            {nudging ? (
                <form
                    className="flex"
                    style={{ gap: 4 }}
                    onSubmit={(e) => {
                        e.preventDefault();
                        sendNudge();
                    }}
                >
                    <input
                        // biome-ignore lint/a11y/noAutofocus: opened on demand
                        autoFocus
                        type="text"
                        placeholder="Optional hint, e.g. check the Board"
                        value={nudge}
                        onChange={(e) => setNudge(e.currentTarget.value)}
                        onKeyDown={(e) =>
                            e.key === 'Escape' && setNudging(false)
                        }
                        style={{ flex: 1, minWidth: 0 }}
                    />
                    <button type="submit" disabled={tick.isPending || busy}>
                        {tick.isPending ? '…' : 'Go'}
                    </button>
                </form>
            ) : null}
            {note ? (
                <div style={{ color: '#a15c00', fontSize: 10 }}>{note}</div>
            ) : null}
        </div>
    );
}

const th: CSSProperties = {
    padding: '2px 6px',
    borderBottom: '1px solid #aca899',
    borderRight: '1px solid #d4d0c8',
    fontWeight: 400,
    textAlign: 'left',
    background: 'linear-gradient(180deg,#fff 0%,#ebeadb 100%)',
    position: 'sticky',
    top: 0,
    whiteSpace: 'nowrap',
};
const td: CSSProperties = {
    padding: '3px 6px',
    verticalAlign: 'top',
    borderBottom: '1px solid #f0efe6',
};

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
    const dmsQuery = useGetWorldsPartyIdMessenger(WORLD_ID, {
        personaId: playerId ?? undefined,
    });
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
        // Memory captures target the room/DM they came from.
        if (a.app === 'messenger' || a.app === 'memory') {
            const dm = dms.find((d) => d.chatGroupId === target);
            if (dm) return () => openDirectChat(dm.chatGroupId, dm.withName);
            // Someone else's DM: read along without being part of it.
            const other = /^to (.+?):/.exec(a.summary ?? '')?.[1];
            if (a.app !== 'messenger' || !other) return null;
            const pair = `${a.personaName} & ${other}`;
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

    return (
        <div className="flex flex-col flex-1 min-w-0">
            <div className={sectionHeader}>
                <span>Activity{filterName ? ` — ${filterName}` : ''}</span>
                {filterId ? (
                    <button
                        type="button"
                        className="xp-bare xp-glass-chip"
                        style={{ color: '#003399', cursor: 'pointer' }}
                        onClick={onClearFilter}
                    >
                        ✕ show everyone
                    </button>
                ) : null}
            </div>
            <div
                className="flex-1 overflow-y-auto"
                style={{ background: '#fff' }}
            >
                {activitiesQuery.isError ? (
                    <p style={{ color: '#c00', padding: 8 }}>
                        Could not load activity.
                    </p>
                ) : null}
                {activities.length === 0 && !activitiesQuery.isPending ? (
                    <p style={{ color: '#666', padding: 12 }}>
                        Nothing has happened yet. Press ▶ Run or ⏩ Tick now to
                        wake someone up.
                    </p>
                ) : null}
                {activities.length > 0 ? (
                    <table
                        style={{
                            width: '100%',
                            borderCollapse: 'collapse',
                            tableLayout: 'fixed',
                        }}
                    >
                        <colgroup>
                            <col style={{ width: 62 }} />
                            <col style={{ width: 96 }} />
                            <col style={{ width: 92 }} />
                            <col />
                        </colgroup>
                        <thead>
                            <tr>
                                <th style={th}>Time</th>
                                <th style={th}>Persona</th>
                                <th style={th}>App</th>
                                <th style={th}>What happened</th>
                            </tr>
                        </thead>
                        <tbody>
                            {activities.map((a) => {
                                const meta = appMeta(a);
                                const open = openFor(a);
                                return (
                                    <tr
                                        key={a.id}
                                        style={{
                                            background:
                                                a.action === 'error'
                                                    ? '#fff4f2'
                                                    : undefined,
                                        }}
                                    >
                                        <td
                                            style={{
                                                ...td,
                                                color: '#666',
                                                whiteSpace: 'nowrap',
                                            }}
                                            title={relTime(a.ts, now)}
                                        >
                                            {new Date(
                                                Number(a.ts),
                                            ).toLocaleTimeString([], {
                                                hour: '2-digit',
                                                minute: '2-digit',
                                            })}
                                        </td>
                                        <td
                                            style={{
                                                ...td,
                                                fontWeight: 600,
                                                overflow: 'hidden',
                                                textOverflow: 'ellipsis',
                                                whiteSpace: 'nowrap',
                                            }}
                                        >
                                            {a.personaName ||
                                                (a.personaId &&
                                                    nameById.get(
                                                        a.personaId,
                                                    )) ||
                                                '—'}
                                        </td>
                                        <td
                                            style={{
                                                ...td,
                                                whiteSpace: 'nowrap',
                                            }}
                                            title={a.action}
                                        >
                                            {meta.icon} {meta.label}
                                        </td>
                                        <td style={td}>
                                            <div
                                                style={{
                                                    wordBreak: 'break-word',
                                                }}
                                            >
                                                {open ? (
                                                    <button
                                                        type="button"
                                                        className="xp-bare"
                                                        style={{
                                                            background: 'none',
                                                            border: 'none',
                                                            padding: 0,
                                                            textAlign: 'left',
                                                            color: 'var(--color-xp-link)',
                                                            textDecoration:
                                                                'underline',
                                                            cursor: 'pointer',
                                                        }}
                                                        onClick={open}
                                                    >
                                                        {a.summary}
                                                    </button>
                                                ) : (
                                                    a.summary
                                                )}
                                            </div>
                                            {a.thought ? (
                                                <div
                                                    style={{
                                                        color: '#777',
                                                        fontStyle: 'italic',
                                                        marginTop: 1,
                                                    }}
                                                >
                                                    “{a.thought}”
                                                </div>
                                            ) : null}
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                ) : null}
            </div>
        </div>
    );
}
