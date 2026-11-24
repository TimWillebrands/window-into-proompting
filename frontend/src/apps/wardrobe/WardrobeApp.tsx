import {
    Columns,
    CommandButton,
    FieldRow,
    GroupBox,
    HudPanel,
    Icon,
    ItemPicker,
    ListView,
    Pane,
    ProgressBar,
    Stack,
    StatusBar,
    StatusBarField,
    Tabs,
    TextBox,
    Toolbar,
    ToolbarButton,
    WindowLayout,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react';
import {
    getGetWorldsPartyIdJobsParticipantIdClothesQueryKey,
    getGetWorldsPartyIdPlacesQueryKey,
    getGetWorldsPartyIdWalletsParticipantIdShopQueryKey,
    getGetWorldsPartyIdWalletsQueryKey,
    useDeleteWorldsPartyIdJobsParticipantIdClothes,
    useGetWorldsPartyIdJobsParticipantIdClothes,
    useGetWorldsPartyIdPlaces,
    useGetWorldsPartyIdWalletsParticipantIdShop,
    useGetWorldsPartyIdWalletsSuspense,
    usePostWorldsPartyIdJobsParticipantIdClothes,
    usePostWorldsPartyIdWalletsParticipantIdBuy,
    usePostWorldsPartyIdWalletsParticipantIdGift,
    usePostWorldsPartyIdWalletsParticipantIdWear,
} from '#api/party-zone';
import { useDesktopContext } from '#lib/desktop-context';
import type { ShopItem, WalletSummary } from '../../api/model';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import {
    AURA_MAX,
    usePlayerAccount,
    usePlayerId,
    useSetAura,
    WORLD_ID,
} from '../../lib/world';
import { lookOf } from '../places/iso/appearance';
import FaceGlyph from '../places/iso/FaceGlyph';
import { avatarColour } from '../places/places-lib';
import {
    inTab,
    isService,
    itemColour,
    itemIcon,
    TABS,
    toggleTry,
    tryOn,
} from './wardrobe-lib';

const DressingRoom = lazy(() => import('../places/scene/DressingRoom'));

const OPENS = 9;
const CLOSES = 21;

const time = (ts: number | string | undefined) =>
    new Date(Number(ts)).toLocaleString([], {
        weekday: 'short',
        hour: '2-digit',
        minute: '2-digit',
    });

type Page = 'shop' | 'closet' | 'receipt';

export default function WardrobeApp({
    participantId,
}: {
    participantId?: string;
}) {
    const wallets = useGetWorldsPartyIdWalletsSuspense(WORLD_ID).data.data;
    const playerId = usePlayerId();
    const { setWindowProps } = useDesktopContext();
    const { windowId } = useWindowInstance();
    const [viewingId, setViewingId] = useState<string | null>(
        participantId ?? playerId ?? wallets[0]?.participantId ?? null,
    );
    useEffect(() => {
        if (participantId) setViewingId(participantId);
    }, [participantId]);
    useEffect(() => {
        if (!viewingId && playerId) setViewingId(playerId);
    }, [viewingId, playerId]);

    const viewing = wallets.find((w) => w.participantId === viewingId);
    const me = wallets.find((w) => w.participantId === playerId);
    const ordered = [...wallets].sort((a, b) =>
        a.participantId === playerId
            ? -1
            : b.participantId === playerId
              ? 1
              : a.name.localeCompare(b.name),
    );

    const select = (id: string) => {
        setViewingId(id);
        setWindowProps(windowId, (p) => ({ ...p, participantId: id }));
    };

    return (
        <WindowLayout
            scroll="none"
            contentLabel="Boutique"
            toolbar={
                <Toolbar labels="below" aria-label="Whose wardrobe">
                    {ordered.map((w) => (
                        <Portrait
                            key={w.participantId}
                            wallet={w}
                            me={w.participantId === playerId}
                            on={w.participantId === viewingId}
                            onClick={() => select(w.participantId)}
                        />
                    ))}
                </Toolbar>
            }
            statusBar={
                <StatusBar>
                    <StatusBarField grow icon={<Icon name="wardrobe" />}>
                        Partytown Boutique · try before you buy · gifts wrapped
                        free
                    </StatusBarField>
                    <OpenSign />
                    {me ? <Purse wallet={me} /> : null}
                </StatusBar>
            }
        >
            {viewing ? (
                <Boutique
                    key={viewing.participantId}
                    viewing={viewing}
                    me={me}
                    playerId={playerId}
                />
            ) : (
                <p className="p-4">Nobody to dress yet.</p>
            )}
        </WindowLayout>
    );
}

function AuraField() {
    const saved = usePlayerAccount().account?.aura ?? '';
    const setAura = useSetAura();
    const [draft, setDraft] = useState(saved);
    const [status, setStatus] = useState<'' | 'saving' | 'saved' | 'failed'>(
        '',
    );
    useEffect(() => setDraft(saved), [saved]);
    const save = async () => {
        if (draft.trim() === saved) return;
        setStatus('saving');
        try {
            await setAura(draft);
            setStatus('saved');
        } catch {
            setStatus('failed');
        }
    };
    return (
        <FieldRow
            layout="stacked"
            label="Aura, felt by whoever stands right next to you"
            hint={
                status === 'saving'
                    ? 'Saving…'
                    : status === 'saved'
                      ? 'Saved'
                      : status === 'failed'
                        ? undefined
                        : `${draft.length}/${AURA_MAX}`
            }
            error={status === 'failed' ? "Couldn't save that" : undefined}
        >
            <TextBox
                maxLength={AURA_MAX}
                value={draft}
                placeholder="e.g. an unsettling calm, like he's not from here"
                onChange={(e) => {
                    setDraft(e.currentTarget.value);
                    setStatus('');
                }}
                onBlur={() => void save()}
                onKeyDown={(e) => {
                    if (e.key === 'Enter') e.currentTarget.blur();
                }}
            />
        </FieldRow>
    );
}

function WorkClothesField({ participantId }: { participantId: string }) {
    const queryClient = useQueryClient();
    const view = useGetWorldsPartyIdJobsParticipantIdClothes(
        WORLD_ID,
        participantId,
    ).data?.data;
    const tailor = usePostWorldsPartyIdJobsParticipantIdClothes();
    const clear = useDeleteWorldsPartyIdJobsParticipantIdClothes();
    const [wish, setWish] = useState('');
    if (!view?.title) return null;
    const refresh = () =>
        Promise.all([
            queryClient.invalidateQueries({
                queryKey: getGetWorldsPartyIdJobsParticipantIdClothesQueryKey(
                    WORLD_ID,
                    participantId,
                ),
            }),
            queryClient.invalidateQueries({
                queryKey: getGetWorldsPartyIdPlacesQueryKey(WORLD_ID),
            }),
        ]);
    const design = async () => {
        if (!wish.trim()) return;
        await tailor.mutateAsync({
            partyId: WORLD_ID,
            participantId,
            data: { text: wish.trim() },
        });
        setWish('');
        await refresh();
    };
    const stop = async () => {
        await clear.mutateAsync({ partyId: WORLD_ID, participantId });
        await refresh();
    };
    const busy = tailor.isPending || clear.isPending;
    const dressed = view.designed && !view.ownClothes;
    return (
        <GroupBox
            title={`Work clothes, as ${view.title}`}
            icon={<Icon name="tie" />}
        >
            <FieldRow
                layout="stacked"
                label="What to wear for work"
                hint={
                    dressed
                        ? `${view.summary ?? 'Designed'}${view.wearing ? ' · wearing them now' : ' · on at the start of your shift'}`
                        : 'You go to work in your own clothes.'
                }
                error={
                    tailor.error || clear.error
                        ? ((tailor.error ?? clear.error) as Error).message
                        : undefined
                }
            >
                <TextBox
                    maxLength={300}
                    value={wish}
                    disabled={busy}
                    placeholder="e.g. chef whites and clogs"
                    onChange={(e) => setWish(e.currentTarget.value)}
                    onKeyDown={(e) => {
                        if (e.key === 'Enter') void design();
                    }}
                />
            </FieldRow>
            <Stack direction="row" wrap gap="sm">
                <CommandButton
                    disabled={busy || !wish.trim()}
                    onClick={() => void design()}
                >
                    {tailor.isPending ? 'Designing…' : 'Design them'}
                </CommandButton>
                {dressed ? (
                    <CommandButton disabled={busy} onClick={() => void stop()}>
                        Come in my own clothes
                    </CommandButton>
                ) : null}
            </Stack>
        </GroupBox>
    );
}

function OpenSign() {
    const hour = new Date().getHours();
    const open = hour >= OPENS && hour < CLOSES;
    return (
        <StatusBarField
            icon={<Icon name={open ? 'sun' : 'moon'} />}
            title={`Open ${OPENS}:00–${CLOSES}:00 for everyone in town`}
        >
            {open ? 'Open' : 'Closed'}
        </StatusBarField>
    );
}

function Purse({ wallet }: { wallet: WalletSummary }) {
    const balance = Number(wallet.balance);
    const [shown, setShown] = useState(balance);
    const [delta, setDelta] = useState<{ n: number; id: number } | null>(null);
    const last = useRef(balance);
    useEffect(() => {
        const from = last.current;
        last.current = balance;
        if (from === balance) return;
        setDelta({ n: balance - from, id: Date.now() });
        const start = performance.now();
        let raf = 0;
        const step = (t: number) => {
            const k = Math.min(1, (t - start) / 600);
            setShown(Math.round(from + (balance - from) * k));
            if (k < 1) raf = requestAnimationFrame(step);
        };
        raf = requestAnimationFrame(step);
        const fade = setTimeout(() => setDelta(null), 1400);
        return () => {
            cancelAnimationFrame(raf);
            clearTimeout(fade);
        };
    }, [balance]);
    return (
        <StatusBarField
            icon={<Icon name="money" />}
            title={`${wallet.means} · +${wallet.dailyPay} a day${wallet.paidToday ? ' (paid today)' : ''}`}
        >
            <span>
                {shown}¢ · +{wallet.dailyPay}/day
                {delta ? (
                    <span key={delta.id}>
                        {' '}
                        ({delta.n > 0 ? '+' : '−'}
                        {Math.abs(delta.n)})
                    </span>
                ) : null}
            </span>
        </StatusBarField>
    );
}

function Portrait({
    wallet,
    me,
    on,
    onClick,
}: {
    wallet: WalletSummary;
    me: boolean;
    on: boolean;
    onClick: () => void;
}) {
    const places = useGetWorldsPartyIdPlaces(WORLD_ID).data?.data;
    const body = places?.bodies?.find(
        (b) => b.participantId === wallet.participantId,
    );
    const look = lookOf(body?.appearance, avatarColour(wallet.participantId));
    return (
        <ToolbarButton
            pressed={on}
            onClick={onClick}
            title={wallet.name}
            icon={<FaceGlyph look={look} size={32} eyes />}
        >
            {wallet.name.split(' ')[0]}
            {me ? ' ★' : ''}
            {wallet.newGifts.length > 0 ? (
                <Icon name="gift" label="has presents" />
            ) : null}
        </ToolbarButton>
    );
}

function Boutique({
    viewing,
    me,
    playerId,
}: {
    viewing: WalletSummary;
    me: WalletSummary | undefined;
    playerId: string | null;
}) {
    const queryClient = useQueryClient();
    const mine = viewing.participantId === playerId;
    const shopOwner = me?.participantId ?? viewing.participantId;
    const shop =
        useGetWorldsPartyIdWalletsParticipantIdShop(WORLD_ID, shopOwner).data
            ?.data ?? [];
    const places = useGetWorldsPartyIdPlaces(WORLD_ID).data?.data;
    const body = places?.bodies?.find(
        (b) => b.participantId === viewing.participantId,
    );
    const [page, setPage] = useState<Page>('shop');
    const [tab, setTab] = useState('all');
    const [tried, setTried] = useState<ShopItem[]>([]);
    const [mood, setMood] = useState<'idle' | 'dance' | 'wave'>('wave');
    const [toast, setToast] = useState<string | null>(null);
    useEffect(() => {
        const t = setTimeout(() => setMood('idle'), 2500);
        return () => clearTimeout(t);
    }, []);
    const celebrate = (text: string) => {
        setMood('dance');
        setToast(text);
        setTimeout(() => setMood('idle'), 3200);
        setTimeout(() => setToast(null), 3200);
    };

    const refresh = async () => {
        await Promise.all([
            queryClient.invalidateQueries({
                queryKey: getGetWorldsPartyIdWalletsQueryKey(WORLD_ID),
            }),
            queryClient.invalidateQueries({
                queryKey: getGetWorldsPartyIdWalletsParticipantIdShopQueryKey(
                    WORLD_ID,
                    shopOwner,
                ),
            }),
            queryClient.invalidateQueries({
                queryKey: getGetWorldsPartyIdPlacesQueryKey(WORLD_ID),
            }),
        ]);
    };
    const buy = usePostWorldsPartyIdWalletsParticipantIdBuy();
    const wear = usePostWorldsPartyIdWalletsParticipantIdWear();
    const gift = usePostWorldsPartyIdWalletsParticipantIdGift();
    const busy = buy.isPending || wear.isPending || gift.isPending;
    const failure = (buy.error ?? wear.error ?? gift.error) as Error | null;
    const purse = Number(me?.balance ?? 0);

    const owned = useMemo(
        () => new Set(viewing.wardrobe.map((i) => i.key)),
        [viewing.wardrobe],
    );
    const preview = useMemo(
        () => tryOn(body?.ownLook ?? body?.appearance, tried),
        [body?.ownLook, body?.appearance, tried],
    );
    const items = (page === 'closet' ? viewing.wardrobe : shop).filter((i) =>
        inTab(i, tab),
    );
    const toBuy = tried.filter(
        (t) =>
            !owned.has(t.key) &&
            shop.some((s) => s.key === t.key) &&
            (mine || !isService(t)),
    );
    const toWear = mine
        ? tried.filter(
              (t) => owned.has(t.key) && !viewing.wearing.includes(t.key),
          )
        : [];
    const total = toBuy.reduce((n, t) => n + Number(t.price), 0);

    const checkout = async () => {
        if (!me) return;
        for (const item of toBuy)
            if (mine)
                await buy.mutateAsync({
                    partyId: WORLD_ID,
                    participantId: me.participantId,
                    data: { key: item.key },
                });
            else
                await gift.mutateAsync({
                    partyId: WORLD_ID,
                    participantId: me.participantId,
                    data: { key: item.key, toId: viewing.participantId },
                });
        for (const item of toWear)
            await wear.mutateAsync({
                partyId: WORLD_ID,
                participantId: viewing.participantId,
                data: { key: item.key },
            });
        await refresh();
        setTried([]);
        celebrate(
            mine
                ? toBuy.length
                    ? `New look! −${total}¢`
                    : 'Looking good!'
                : `Wrapped and sent to ${viewing.name.split(' ')[0]}`,
        );
    };

    const catalogue = (
        <Stack gap="sm" className="h-full">
            <Toolbar aria-label="Departments">
                {TABS.map((t) => (
                    <ToolbarButton
                        key={t.id}
                        icon={<Icon name={t.icon} />}
                        pressed={tab === t.id}
                        onClick={() => setTab(t.id)}
                    >
                        {t.label}
                    </ToolbarButton>
                ))}
            </Toolbar>
            <ItemPicker
                label={page === 'closet' ? 'Wardrobe' : 'Shop today'}
                className="min-h-0 flex-1 overflow-y-auto"
                empty={page === 'closet' ? 'Nothing here yet.' : 'Sold out!'}
                selected={tried.map((t) => t.key)}
                onToggle={(key) => {
                    const item = items.find((i) => i.key === key);
                    if (item) setTried((x) => toggleTry(x, item));
                }}
                items={items.map((item) => {
                    const trying = tried.some((t) => t.key === item.key);
                    const wearing = viewing.wearing.includes(item.key);
                    const has = owned.has(item.key);
                    return {
                        id: item.key,
                        label: item.name,
                        picture: itemIcon(item),
                        swatch: itemColour(item),
                        detail: page === 'shop' ? `${item.price}¢` : undefined,
                        detailTone:
                            page === 'shop' && Number(item.price) > purse
                                ? 'danger'
                                : undefined,
                        badge: wearing
                            ? 'wearing'
                            : has && page === 'shop'
                              ? 'owned'
                              : undefined,
                        badgeTone: wearing ? 'go' : 'neutral',
                        tag: isService(item) ? 'salon' : undefined,
                        hint: trying ? 'Take it off' : 'Try it on',
                    };
                })}
            />
        </Stack>
    );

    return (
        <Columns gap="lg" pad="md" narrowBelow={520}>
            <Columns.Column
                as="section"
                aria-label="Fitting room"
                weight={0.85}
                minWidth="220px"
            >
                <Pane>
                    <Stack gap="sm" className="h-full">
                        <Stack
                            direction="row"
                            grow
                            className="relative min-h-[260px]"
                        >
                            <Suspense
                                fallback={
                                    <Stack grow align="center" justify="center">
                                        <ProgressBar
                                            marquee
                                            label="Setting up the mirror"
                                        />
                                    </Stack>
                                }
                            >
                                <DressingRoom
                                    appearance={preview}
                                    fallbackColour={avatarColour(
                                        viewing.participantId,
                                    )}
                                    mood={mood}
                                />
                            </Suspense>
                            <Stack
                                align="start"
                                justify="between"
                                pad="md"
                                className="pointer-events-none absolute inset-0"
                            >
                                <HudPanel title={mine ? 'You' : viewing.name}>
                                    {viewing.means}
                                </HudPanel>
                                <HudPanel className="pointer-events-none self-center">
                                    <Stack
                                        as="span"
                                        direction="row"
                                        align="center"
                                        gap="xs"
                                    >
                                        <Icon name="refresh" />
                                        drag to turn
                                    </Stack>
                                </HudPanel>
                            </Stack>
                            {toast ? (
                                <HudPanel
                                    anchor="top"
                                    role="status"
                                    icon={
                                        <Icon
                                            name={mine ? 'sparkle' : 'gift'}
                                        />
                                    }
                                    title={toast}
                                />
                            ) : null}
                        </Stack>
                        <GroupBox
                            title="Trying on"
                            icon={<Icon name="shirt" />}
                        >
                            {tried.length === 0 ? (
                                <span>
                                    Click anything to try it on{' '}
                                    {mine
                                        ? 'yourself'
                                        : `${viewing.name.split(' ')[0]}`}
                                </span>
                            ) : (
                                <ItemPicker
                                    size="small"
                                    label="Trying on"
                                    selected={tried.map((t) => t.key)}
                                    onToggle={(key) => {
                                        const item = tried.find(
                                            (t) => t.key === key,
                                        );
                                        if (item)
                                            setTried((x) => toggleTry(x, item));
                                    }}
                                    items={tried.map((t) => ({
                                        id: t.key,
                                        label: `Take off ${t.name}`,
                                        picture: itemIcon(t),
                                        swatch: itemColour(t),
                                        hint: `Take off ${t.name}`,
                                    }))}
                                />
                            )}
                        </GroupBox>
                        <Stack direction="row" gap="sm">
                            <CommandButton
                                className="flex-1"
                                icon={<Icon name="undo" />}
                                disabled={tried.length === 0}
                                onClick={() => setTried([])}
                            >
                                Undo
                            </CommandButton>
                            {me && (toBuy.length > 0 || toWear.length > 0) ? (
                                <CommandButton
                                    className="flex-1"
                                    tone="go"
                                    icon={
                                        mine ? undefined : <Icon name="gift" />
                                    }
                                    disabled={busy || total > purse}
                                    onClick={() => void checkout()}
                                >
                                    {mine
                                        ? toBuy.length
                                            ? `Buy the look · ${total}¢`
                                            : 'Wear it'
                                        : `Give it · ${total}¢`}
                                </CommandButton>
                            ) : null}
                        </Stack>
                        {total > purse ? (
                            <Stack
                                as="p"
                                direction="row"
                                align="center"
                                gap="xs"
                            >
                                <Icon name="warning" />
                                That's {total - purse}¢ more than you have.
                            </Stack>
                        ) : null}
                        {failure ? (
                            <Stack
                                as="p"
                                direction="row"
                                align="center"
                                gap="xs"
                            >
                                <Icon name="error" />
                                {failure.message}
                            </Stack>
                        ) : null}
                        {mine ? <AuraField /> : null}
                        {mine ? (
                            <WorkClothesField
                                participantId={viewing.participantId}
                            />
                        ) : null}
                    </Stack>
                </Pane>
            </Columns.Column>
            <Columns.Column
                as="section"
                aria-label="Catalogue"
                weight={1.15}
                gap="sm"
            >
                {viewing.newGifts.length > 0 ? (
                    <Presents wallet={viewing} />
                ) : null}
                <Tabs
                    label="Boutique"
                    className="min-h-0 flex-1"
                    value={page}
                    onValueChange={(v) => setPage(v as Page)}
                    items={[
                        {
                            value: 'shop',
                            label: 'Shop today',
                            icon: <Icon name="shopping" />,
                            content: catalogue,
                        },
                        {
                            value: 'closet',
                            label: `${mine ? 'My' : `${viewing.name.split(' ')[0]}'s`} wardrobe (${viewing.wardrobe.length})`,
                            icon: <Icon name="door" />,
                            content: catalogue,
                        },
                        {
                            value: 'receipt',
                            label: 'Receipts',
                            icon: <Icon name="receipt" />,
                            content: <Receipt wallet={viewing} />,
                        },
                    ]}
                />
            </Columns.Column>
        </Columns>
    );
}

function Presents({ wallet }: { wallet: WalletSummary }) {
    const [opened, setOpened] = useState<string[]>([]);
    const key = (g: WalletSummary['newGifts'][number]) => `${g.fromId}:${g.at}`;
    return (
        <GroupBox title="Presents" icon={<Icon name="gift" />}>
            {wallet.newGifts.map((g) => {
                const open = opened.includes(key(g));
                return (
                    <CommandButton
                        key={key(g)}
                        className="justify-start justify-self-start"
                        icon={<Icon name={open ? 'sparkle' : 'gift'} />}
                        onClick={() => setOpened((o) => [...o, key(g)])}
                    >
                        {open ? (
                            <span>
                                <b>{g.item}</b> from {g.fromName}
                            </span>
                        ) : (
                            <span>
                                A present from <b>{g.fromName}</b>, click to
                                unwrap
                            </span>
                        )}
                    </CommandButton>
                );
            })}
        </GroupBox>
    );
}

function Receipt({ wallet }: { wallet: WalletSummary }) {
    return (
        <Stack gap="md" className="h-full">
            <p>
                <b>Partytown Boutique</b> · {wallet.name}
            </p>
            <ListView
                view="details"
                selectionMode="none"
                label="Receipts"
                className="min-h-0 flex-1"
                empty="Nothing yet."
                columns={[
                    {
                        key: 'label',
                        header: 'When',
                        width: 'minmax(110px, 1fr)',
                    },
                    { key: 'reason', header: 'What', width: 'minmax(0, 2fr)' },
                    {
                        key: 'amount',
                        header: 'Amount',
                        align: 'end',
                        width: 'minmax(64px, auto)',
                    },
                ]}
                items={wallet.ledger.map((e) => ({
                    id: `${e.at}-${e.reason}`,
                    label: time(e.at),
                    cells: {
                        reason: e.reason,
                        amount: `${Number(e.amount) > 0 ? '+' : ''}${e.amount}`,
                    },
                }))}
            />
            <Stack as="p" direction="row" justify="between" gap="md">
                <span>Balance</span>
                <b>{wallet.balance}¢</b>
            </Stack>
            <p>Thank you for shopping · {wallet.means}</p>
        </Stack>
    );
}
