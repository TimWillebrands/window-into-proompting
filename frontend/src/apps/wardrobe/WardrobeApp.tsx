import { useQueryClient } from '@tanstack/react-query';
import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react';
import {
    getGetWorldsPartyIdPlacesQueryKey,
    getGetWorldsPartyIdWalletsParticipantIdShopQueryKey,
    getGetWorldsPartyIdWalletsQueryKey,
    useGetWorldsPartyIdPlaces,
    useGetWorldsPartyIdWalletsParticipantIdShop,
    useGetWorldsPartyIdWalletsSuspense,
    usePostWorldsPartyIdWalletsParticipantIdBuy,
    usePostWorldsPartyIdWalletsParticipantIdGift,
    usePostWorldsPartyIdWalletsParticipantIdWear,
} from '#api/party-zone';
import { useDesktopContext } from '#lib/desktop-context';
import type { ShopItem, WalletSummary } from '../../api/model';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import { usePlayerId, WORLD_ID } from '../../lib/world';
import { lookOf } from '../places/iso/appearance';
import { avatarColour } from '../places/places-lib';
import './wardrobe.css';
import {
    inTab,
    isService,
    itemColour,
    itemIcon,
    TABS,
    toggleTry,
    tryOn,
} from './wardrobe-lib';

const DressingRoom = lazy(() => import('./DressingRoom'));

const OPENS = 9;
const CLOSES = 21;

const time = (ts: number | string | undefined) =>
    new Date(Number(ts)).toLocaleString([], {
        weekday: 'short',
        hour: '2-digit',
        minute: '2-digit',
    });

type Page = 'shop' | 'closet' | 'receipt';

/** Wardrobe (ADR 0024): a boutique with a fitting room. Try things on in 3D, buy the outfit, dress a friend and give it to them. */
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
        <div className="wr-app">
            <header className="wr-awning">
                <div className="wr-brand">
                    <span className="wr-logo">👗</span>
                    <div>
                        <b>Partytown Boutique</b>
                        <small>try before you buy · gifts wrapped free</small>
                    </div>
                </div>
                <OpenSign />
                {me ? <Purse wallet={me} /> : null}
            </header>
            <nav className="wr-people" aria-label="Whose wardrobe">
                {ordered.map((w) => (
                    <Portrait
                        key={w.participantId}
                        wallet={w}
                        me={w.participantId === playerId}
                        on={w.participantId === viewingId}
                        onClick={() => select(w.participantId)}
                    />
                ))}
            </nav>
            {viewing ? (
                <Boutique
                    key={viewing.participantId}
                    viewing={viewing}
                    me={me}
                    playerId={playerId}
                />
            ) : (
                <div className="wr-empty">Nobody to dress yet.</div>
            )}
        </div>
    );
}

function OpenSign() {
    const hour = new Date().getHours();
    const open = hour >= OPENS && hour < CLOSES;
    return (
        <span
            className="wr-sign"
            data-open={open}
            title={`Open ${OPENS}:00–${CLOSES}:00 for everyone in town`}
        >
            {open ? 'OPEN' : '🌙 CLOSED'}
        </span>
    );
}

/** Your purse; the number rolls when it changes and a −/+ floats up. */
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
        return () => cancelAnimationFrame(raf);
    }, [balance]);
    return (
        <div
            className="wr-purse"
            title={`${wallet.means} · +${wallet.dailyPay} a day${wallet.paidToday ? ' (paid today)' : ''}`}
        >
            <span className="wr-coin">¢</span>
            <b>{shown}</b>
            <small>+{wallet.dailyPay}/day</small>
            {delta ? (
                <span key={delta.id} className="wr-delta" data-up={delta.n > 0}>
                    {delta.n > 0 ? '+' : '−'}
                    {Math.abs(delta.n)}
                </span>
            ) : null}
        </div>
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
        <button
            type="button"
            className="wr-portrait"
            aria-pressed={on}
            onClick={onClick}
            title={wallet.name}
        >
            <span
                className="wr-face"
                style={{
                    background: look.skin,
                    boxShadow: `inset 0 9px 0 ${look.hair}, 0 0 0 3px ${look.top}`,
                }}
            >
                <i />
                <i />
            </span>
            <span>
                {wallet.name.split(' ')[0]}
                {me ? ' ★' : ''}
            </span>
            {wallet.newGifts.length > 0 ? (
                <em className="wr-badge">🎁</em>
            ) : null}
        </button>
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
    // The Player pays, so it's their window display; without a Player, show whoever we're looking at.
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
        () => tryOn(body?.appearance, tried),
        [body?.appearance, tried],
    );
    const items = (page === 'closet' ? viewing.wardrobe : shop).filter((i) =>
        inTab(i, tab),
    );
    // What the tried-on outfit would cost: shop items you don't own yet.
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
                : `🎁 Wrapped and sent to ${viewing.name.split(' ')[0]}`,
        );
    };

    return (
        <div className="wr-body">
            <section className="wr-fitting">
                <Suspense fallback={<div className="wr-stage" />}>
                    <DressingRoom
                        appearance={preview}
                        fallbackColour={avatarColour(viewing.participantId)}
                        mood={mood}
                    />
                </Suspense>
                <div className="wr-mirror-name">
                    {mine ? 'You' : viewing.name}
                    <small>{viewing.means}</small>
                </div>
                {toast ? <div className="wr-toast">{toast}</div> : null}
                <div className="wr-tray">
                    {tried.length === 0 ? (
                        <span className="wr-tray-hint">
                            Click anything to try it on{' '}
                            {mine
                                ? 'yourself'
                                : `${viewing.name.split(' ')[0]}`}
                        </span>
                    ) : (
                        tried.map((t) => (
                            <button
                                key={t.key}
                                type="button"
                                className="wr-tray-item"
                                title={`Take off ${t.name}`}
                                onClick={() => setTried((x) => toggleTry(x, t))}
                            >
                                <span style={{ background: itemColour(t) }}>
                                    {itemIcon(t)}
                                </span>
                                ×
                            </button>
                        ))
                    )}
                </div>
                <div className="wr-checkout">
                    <button
                        type="button"
                        className="wr-btn wr-btn--ghost"
                        disabled={tried.length === 0}
                        onClick={() => setTried([])}
                    >
                        ↺ Undo
                    </button>
                    {me && (toBuy.length > 0 || toWear.length > 0) ? (
                        <button
                            type="button"
                            className="wr-btn wr-btn--buy"
                            disabled={busy || total > purse}
                            onClick={() => void checkout()}
                        >
                            {mine
                                ? toBuy.length
                                    ? `Buy the look · ${total}¢`
                                    : 'Wear it'
                                : `🎁 Give it · ${total}¢`}
                        </button>
                    ) : null}
                </div>
                {total > purse ? (
                    <div className="wr-warn">
                        That's {total - purse}¢ more than you have.
                    </div>
                ) : null}
                {failure ? (
                    <div className="wr-warn">{failure.message}</div>
                ) : null}
            </section>
            <section className="wr-catalogue">
                {viewing.newGifts.length > 0 ? (
                    <Presents wallet={viewing} />
                ) : null}
                <div className="wr-pages" role="tablist">
                    {(
                        [
                            ['shop', '🛍️ Shop today'],
                            [
                                'closet',
                                `🚪 ${mine ? 'My' : `${viewing.name.split(' ')[0]}'s`} wardrobe (${viewing.wardrobe.length})`,
                            ],
                            ['receipt', '🧾 Receipts'],
                        ] as const
                    ).map(([id, label]) => (
                        <button
                            key={id}
                            type="button"
                            role="tab"
                            aria-selected={page === id}
                            onClick={() => setPage(id)}
                        >
                            {label}
                        </button>
                    ))}
                </div>
                {page === 'receipt' ? (
                    <Receipt wallet={viewing} />
                ) : (
                    <>
                        <div className="wr-tabs">
                            {TABS.map((t) => (
                                <button
                                    key={t.id}
                                    type="button"
                                    aria-pressed={tab === t.id}
                                    onClick={() => setTab(t.id)}
                                >
                                    <b>{t.icon}</b>
                                    {t.label}
                                </button>
                            ))}
                        </div>
                        <div className="wr-grid">
                            {items.length === 0 ? (
                                <div className="wr-empty">
                                    {page === 'closet'
                                        ? 'Nothing here yet.'
                                        : 'Sold out!'}
                                </div>
                            ) : null}
                            {items.map((item, i) => {
                                const trying = tried.some(
                                    (t) => t.key === item.key,
                                );
                                const wearing = viewing.wearing.includes(
                                    item.key,
                                );
                                const has = owned.has(item.key);
                                return (
                                    <button
                                        key={item.key}
                                        type="button"
                                        className="wr-tile"
                                        data-trying={trying}
                                        style={{
                                            animationDelay: `${i * 30}ms`,
                                        }}
                                        onClick={() =>
                                            setTried((x) => toggleTry(x, item))
                                        }
                                        title={
                                            trying ? 'Take it off' : 'Try it on'
                                        }
                                    >
                                        <span
                                            className="wr-swatch"
                                            style={{
                                                background: `radial-gradient(circle at 35% 30%, #fff8, ${itemColour(item)} 60%)`,
                                            }}
                                        >
                                            {itemIcon(item)}
                                        </span>
                                        <span className="wr-tile-name">
                                            {item.name}
                                        </span>
                                        {page === 'shop' ? (
                                            <span
                                                className="wr-price"
                                                data-dear={
                                                    Number(item.price) > purse
                                                }
                                            >
                                                {item.price}¢
                                            </span>
                                        ) : null}
                                        {wearing ? (
                                            <span className="wr-ribbon">
                                                wearing
                                            </span>
                                        ) : has && page === 'shop' ? (
                                            <span className="wr-ribbon wr-ribbon--owned">
                                                owned
                                            </span>
                                        ) : null}
                                        {isService(item) ? (
                                            <span className="wr-salon">
                                                salon
                                            </span>
                                        ) : null}
                                        {trying ? (
                                            <span className="wr-trying">
                                                👀
                                            </span>
                                        ) : null}
                                    </button>
                                );
                            })}
                        </div>
                    </>
                )}
            </section>
        </div>
    );
}

/** Presents arrive wrapped; unwrap them one by one. */
function Presents({ wallet }: { wallet: WalletSummary }) {
    const [opened, setOpened] = useState<string[]>([]);
    const key = (g: WalletSummary['newGifts'][number]) => `${g.fromId}:${g.at}`;
    return (
        <div className="wr-presents">
            {wallet.newGifts.map((g) => {
                const open = opened.includes(key(g));
                return (
                    <button
                        key={key(g)}
                        type="button"
                        className="wr-present"
                        data-open={open}
                        onClick={() => setOpened((o) => [...o, key(g)])}
                    >
                        <span className="wr-box">{open ? '✨' : '🎁'}</span>
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
                    </button>
                );
            })}
        </div>
    );
}

function Receipt({ wallet }: { wallet: WalletSummary }) {
    return (
        <div className="wr-receipt">
            <div className="wr-receipt-head">
                PARTYTOWN BOUTIQUE
                <small>{wallet.name}</small>
            </div>
            {wallet.ledger.length === 0 ? (
                <div style={{ textAlign: 'center' }}>— nothing yet —</div>
            ) : null}
            {wallet.ledger.map((e) => (
                <div key={`${e.at}-${e.reason}`} className="wr-receipt-line">
                    <span>{time(e.at)}</span>
                    <span>{e.reason}</span>
                    <b data-up={Number(e.amount) > 0}>
                        {Number(e.amount) > 0 ? '+' : ''}
                        {e.amount}
                    </b>
                </div>
            ))}
            <div className="wr-receipt-line wr-receipt-total">
                <span />
                <span>BALANCE</span>
                <b>{wallet.balance}¢</b>
            </div>
            <div className="wr-receipt-foot">
                thank you for shopping · {wallet.means}
            </div>
        </div>
    );
}
