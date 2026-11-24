import type { Meta, StoryObj } from '@storybook/react-vite';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode, useState } from 'react';
import {
    getGetWorldsPartyIdJobsParticipantIdClothesQueryKey,
    getGetWorldsPartyIdPlacesQueryKey,
    getGetWorldsPartyIdWalletsParticipantIdShopQueryKey,
    getGetWorldsPartyIdWalletsQueryKey,
} from '#api/party-zone';
import type { ShopItem } from '../../api/model/shopItem';
import type { WalletSummary } from '../../api/model/walletSummary';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WindowInstanceProvider } from '../../components/desktop/window-instance-context';
import { usePlayerSession, WORLD_ID } from '../../lib/world';
import WardrobeApp from './WardrobeApp';

const ME = 'player-1';
const NOW = Date.parse('2026-11-22T14:00:00Z');

const item = (
    key: string,
    slot: string,
    name: string,
    price: number,
    picks: Record<string, string>,
): ShopItem => ({ key, slot, name, price, picks }) as ShopItem;

const shop: ShopItem[] = [
    item('tee-navy', 'top', 'Navy band tee', 12, {
        top: 'navy',
        topStyle: 'band-tee',
    }),
    item('hoodie-teal', 'top', 'Teal hoodie', 24, {
        top: 'teal',
        topStyle: 'hoodie',
    }),
    item('jeans', 'bottom', 'Baggy jeans', 18, {
        bottom: 'blue',
        bottomStyle: 'baggy-jeans',
    }),
    item('boots', 'shoes', 'Platform boots', 90, {
        shoes: 'black',
        shoeStyle: 'platforms',
    }),
    item('cap', 'accessory', 'Red cap', 8, { accessory: 'cap' }),
    item('pixie', 'hairStyle', 'Pixie cut', 25, { hairStyle: 'pixie' }),
    item('pink', 'hair', 'Pink dye', 15, { hair: 'pink' }),
];

const wallet = (
    participantId: string,
    name: string,
    extra: Partial<WalletSummary> = {},
): WalletSummary => ({
    participantId,
    name,
    balance: 64,
    means: 'a barista’s wage',
    dailyPay: 12,
    paidToday: true,
    wardrobe: [shop[0], shop[2]],
    ledger: [
        { amount: 12, reason: 'Daily pay', at: NOW - 3_600_000 },
        { amount: -18, reason: 'Baggy jeans', at: NOW - 7_200_000 },
    ],
    newGifts: [],
    wearing: ['tee-navy'],
    ...extra,
});

const wallets: WalletSummary[] = [
    wallet(ME, 'Ana Lopez', {
        newGifts: [
            { fromId: 'bo', fromName: 'Bo Berg', item: 'Red cap', at: NOW },
        ],
    }),
    wallet('bo', 'Bo Berg', { balance: 12, wearing: [] }),
    wallet('cy', 'Cy Dorn', { wardrobe: [] }),
];

const ok = <T,>(data: T) => ({ data, status: 200, headers: new Headers() });

function seed(client: QueryClient) {
    client.setQueryData(
        getGetWorldsPartyIdWalletsQueryKey(WORLD_ID),
        ok(wallets),
    );
    for (const w of wallets)
        client.setQueryData(
            getGetWorldsPartyIdWalletsParticipantIdShopQueryKey(
                WORLD_ID,
                w.participantId,
            ),
            ok(shop),
        );
    client.setQueryData(
        getGetWorldsPartyIdPlacesQueryKey(WORLD_ID),
        ok({
            places: [],
            bodies: wallets.map((w) => ({
                participantId: w.participantId,
                name: w.name,
                appearance: {
                    skin: 'fair',
                    hair: 'brown',
                    hairStyle: 'short',
                    top: 'teal',
                },
            })),
            now: NOW,
        }),
    );
    client.setQueryData(
        getGetWorldsPartyIdJobsParticipantIdClothesQueryKey(WORLD_ID, ME),
        ok({
            title: 'Barista',
            designed: true,
            ownClothes: false,
            summary: 'A green apron over a white shirt',
            wearing: false,
            onShift: false,
        }),
    );
    client.setQueryData(['world-player', WORLD_ID, 'user-1'], {
        playerId: ME,
        name: 'Ana Lopez',
        aura: 'an unsettling calm',
    });
}

function Seeded({ children }: { children: ReactNode }) {
    const [client] = useState(() => {
        const fresh = storyClient();
        seed(fresh);
        return fresh;
    });
    return (
        <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
}

const storyClient = () =>
    new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                staleTime: Number.POSITIVE_INFINITY,
                gcTime: Number.POSITIVE_INFINITY,
            },
            mutations: { retry: false },
        },
    });

const meta = {
    title: 'Apps/WardrobeApp',
    component: WardrobeApp,
    parameters: { layout: 'fullscreen' },
    beforeEach: () => {
        const before = usePlayerSession.getState();
        usePlayerSession.setState({ userId: 'user-1', hydrated: true });
        return () =>
            usePlayerSession.setState({
                userId: before.userId,
                hydrated: before.hydrated,
            });
    },
    decorators: [
        (Story, context) => {
            const width = (context.parameters.width as number) ?? 960;
            return (
                <Seeded>
                    <WindowInstanceProvider windowId="wardrobe">
                        <div
                            className="h-[720px] p-6"
                            style={{ width: width + 40 }}
                        >
                            {/* biome-ignore lint/correctness/useUniqueElementIds: stories render a single instance */}
                            <StoryWindow
                                id="wardrobe"
                                title="Wardrobe"
                                icon="/img/icons/wardrobe.ico"
                            >
                                <Story />
                            </StoryWindow>
                        </div>
                    </WindowInstanceProvider>
                </Seeded>
            );
        },
    ],
} satisfies Meta<typeof WardrobeApp>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Mine: Story = {};

export const TryingOn: Story = {
    play: async ({ canvasElement }) => {
        await new Promise((r) => setTimeout(r, 500));
        for (const label of ['Teal hoodie', 'Platform boots'])
            Array.from(
                canvasElement.querySelectorAll<HTMLButtonElement>(
                    '.ui-item-picker-item',
                ),
            )
                .find((b) => b.textContent?.includes(label))
                ?.click();
    },
};

export const Someone: Story = { args: { participantId: 'bo' } };

export const Narrow: Story = { parameters: { width: 480 } };

export const Receipts: Story = {
    play: async ({ canvasElement }) => {
        await new Promise((r) => setTimeout(r, 500));
        Array.from(
            canvasElement.querySelectorAll<HTMLButtonElement>('[role="tab"]'),
        )
            .find((b) => b.textContent?.includes('Receipts'))
            ?.click();
    },
};
