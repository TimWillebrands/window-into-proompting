import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useState } from 'react';
import {
    getGetWorldsPartyIdWalletsParticipantIdShopQueryKey,
    getGetWorldsPartyIdWalletsQueryKey,
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

const SLOT_LABEL: Record<string, string> = {
    hair: 'Hair colour',
    hairStyle: 'Haircut',
    top: 'Top',
    bottom: 'Bottom',
    shoes: 'Shoes',
    accessory: 'Extra',
};

const isService = (item: ShopItem) =>
    item.slot === 'hair' || item.slot === 'hairStyle';

const time = (ts: number | string | undefined) =>
    new Date(Number(ts)).toLocaleString([], {
        weekday: 'short',
        hour: '2-digit',
        minute: '2-digit',
    });

/** Wardrobe: anyone's money and clothes; the Player can wear, buy and give (ADR 0024). */
export default function WardrobeApp({
    participantId,
}: {
    participantId?: string;
}) {
    const wallets = useGetWorldsPartyIdWalletsSuspense(WORLD_ID).data.data;
    const playerId = usePlayerId();
    const { setWindowProps } = useDesktopContext();
    const { windowId } = useWindowInstance();
    const pickerId = useId();
    const [selectedId, setSelectedId] = useState<string | null>(
        participantId ?? playerId ?? wallets[0]?.participantId ?? null,
    );

    useEffect(() => {
        if (participantId) setSelectedId(participantId);
    }, [participantId]);
    useEffect(() => {
        if (!selectedId && playerId) setSelectedId(playerId);
    }, [selectedId, playerId]);

    const select = (id: string) => {
        setSelectedId(id);
        setWindowProps(windowId, (p) => ({ ...p, participantId: id }));
    };

    const wallet = wallets.find((w) => w.participantId === selectedId);

    return (
        <div className="app-surface flex flex-col h-full">
            <div
                className="flex items-center"
                style={{
                    gap: 6,
                    padding: '3px 6px',
                    background: 'var(--color-xp-face)',
                    borderBottom: '1px solid #aca899',
                }}
            >
                <label htmlFor={pickerId}>Wardrobe of:</label>
                <select
                    id={pickerId}
                    value={selectedId ?? ''}
                    onChange={(e) => select(e.currentTarget.value)}
                >
                    {selectedId ? null : <option value="">— pick —</option>}
                    {wallets.map((w) => (
                        <option key={w.participantId} value={w.participantId}>
                            {w.name}
                            {w.participantId === playerId ? ' (you)' : ''}
                        </option>
                    ))}
                </select>
                {wallet ? (
                    <span style={{ marginLeft: 'auto' }}>
                        💰 <b>{wallet.balance}</b> credits · {wallet.means} · +
                        {wallet.dailyPay}/day
                        {wallet.paidToday ? ' (paid today)' : ''}
                    </span>
                ) : null}
            </div>
            {wallet ? (
                <WalletPage
                    wallet={wallet}
                    others={wallets.filter(
                        (w) => w.participantId !== wallet.participantId,
                    )}
                    canAct={wallet.participantId === playerId}
                />
            ) : (
                <div className="flex-1" style={{ padding: 12, color: '#666' }}>
                    Nobody to show yet.
                </div>
            )}
        </div>
    );
}

function WalletPage({
    wallet,
    others,
    canAct,
}: {
    wallet: WalletSummary;
    others: WalletSummary[];
    canAct: boolean;
}) {
    const queryClient = useQueryClient();
    const id = wallet.participantId;
    const shop = useGetWorldsPartyIdWalletsParticipantIdShop(WORLD_ID, id);
    const refresh = async () => {
        await queryClient.invalidateQueries({
            queryKey: getGetWorldsPartyIdWalletsQueryKey(WORLD_ID),
        });
        await queryClient.invalidateQueries({
            queryKey: getGetWorldsPartyIdWalletsParticipantIdShopQueryKey(
                WORLD_ID,
                id,
            ),
        });
    };
    const options = { mutation: { onSuccess: refresh } };
    const wear = usePostWorldsPartyIdWalletsParticipantIdWear(options);
    const buy = usePostWorldsPartyIdWalletsParticipantIdBuy(options);
    const gift = usePostWorldsPartyIdWalletsParticipantIdGift(options);
    const busy = wear.isPending || buy.isPending || gift.isPending;
    const failure = (wear.error ?? buy.error ?? gift.error) as Error | null;
    const balance = Number(wallet.balance);

    return (
        <div className="flex-1 overflow-y-auto" style={{ padding: 8 }}>
            {wallet.newGifts.length > 0 ? (
                <div
                    style={{
                        background: '#ffffe1',
                        border: '1px solid #aca899',
                        padding: '4px 8px',
                        marginBottom: 8,
                    }}
                >
                    🎁{' '}
                    {wallet.newGifts
                        .map((g) => `${g.item} from ${g.fromName}`)
                        .join(', ')}
                </div>
            ) : null}
            {failure ? (
                <div style={{ color: '#a00', marginBottom: 8 }}>
                    {failure.message}
                </div>
            ) : null}
            <div className="flex" style={{ gap: 8, alignItems: 'flex-start' }}>
                <fieldset style={{ flex: 1 }}>
                    <legend>In the wardrobe</legend>
                    {wallet.wardrobe.length === 0 ? (
                        <div style={{ color: '#666' }}>Nothing yet.</div>
                    ) : null}
                    {wallet.wardrobe.map((item) => {
                        const on = wallet.wearing.includes(item.key);
                        return (
                            <ItemRow key={item.key} item={item}>
                                {on ? (
                                    <span style={{ color: '#2a7a2a' }}>
                                        ✓ wearing
                                    </span>
                                ) : canAct ? (
                                    <button
                                        type="button"
                                        disabled={busy}
                                        onClick={() =>
                                            wear.mutate({
                                                partyId: WORLD_ID,
                                                participantId: id,
                                                data: { key: item.key },
                                            })
                                        }
                                    >
                                        Wear
                                    </button>
                                ) : null}
                            </ItemRow>
                        );
                    })}
                </fieldset>
                <fieldset style={{ flex: 1 }}>
                    <legend>In the shops today</legend>
                    {shop.isPending ? 'Loading…' : null}
                    {(shop.data?.data ?? []).map((item) => (
                        <ItemRow key={item.key} item={item} price>
                            {canAct ? (
                                <>
                                    <button
                                        type="button"
                                        disabled={
                                            busy || balance < Number(item.price)
                                        }
                                        onClick={() =>
                                            buy.mutate({
                                                partyId: WORLD_ID,
                                                participantId: id,
                                                data: { key: item.key },
                                            })
                                        }
                                    >
                                        {isService(item) ? 'Get it' : 'Buy'}
                                    </button>
                                    {isService(item) ? null : (
                                        <GiveTo
                                            others={others}
                                            disabled={
                                                busy ||
                                                balance < Number(item.price)
                                            }
                                            onGive={(toId) =>
                                                gift.mutate({
                                                    partyId: WORLD_ID,
                                                    participantId: id,
                                                    data: {
                                                        key: item.key,
                                                        toId,
                                                    },
                                                })
                                            }
                                        />
                                    )}
                                </>
                            ) : null}
                        </ItemRow>
                    ))}
                </fieldset>
            </div>
            <fieldset style={{ marginTop: 8 }}>
                <legend>Recent money</legend>
                {wallet.ledger.length === 0 ? (
                    <div style={{ color: '#666' }}>Nothing spent yet.</div>
                ) : null}
                {wallet.ledger.map((e) => (
                    <div
                        key={`${e.at}-${e.reason}`}
                        className="flex"
                        style={{ gap: 8 }}
                    >
                        <span style={{ color: '#666', width: 90 }}>
                            {time(e.at)}
                        </span>
                        <span style={{ flex: 1 }}>{e.reason}</span>
                        <span
                            style={{
                                color:
                                    Number(e.amount) < 0 ? '#a00' : '#2a7a2a',
                                width: 50,
                                textAlign: 'right',
                            }}
                        >
                            {Number(e.amount) > 0 ? '+' : ''}
                            {e.amount}
                        </span>
                    </div>
                ))}
            </fieldset>
        </div>
    );
}

function ItemRow({
    item,
    price,
    children,
}: {
    item: ShopItem;
    price?: boolean;
    children?: React.ReactNode;
}) {
    return (
        <div className="flex items-center" style={{ gap: 6, padding: '2px 0' }}>
            <span style={{ color: '#666', width: 76 }}>
                {SLOT_LABEL[item.slot] ?? item.slot}
            </span>
            <span style={{ flex: 1 }}>{item.name}</span>
            {price ? (
                <span style={{ color: '#666' }}>{item.price} cr</span>
            ) : null}
            {children}
        </div>
    );
}

function GiveTo({
    others,
    disabled,
    onGive,
}: {
    others: WalletSummary[];
    disabled: boolean;
    onGive: (toId: string) => void;
}) {
    return (
        <select
            aria-label="Give as a present"
            value=""
            disabled={disabled}
            onChange={(e) => {
                if (e.currentTarget.value) onGive(e.currentTarget.value);
            }}
        >
            <option value="">Give…</option>
            {others.map((o) => (
                <option key={o.participantId} value={o.participantId}>
                    {o.name}
                </option>
            ))}
        </select>
    );
}
