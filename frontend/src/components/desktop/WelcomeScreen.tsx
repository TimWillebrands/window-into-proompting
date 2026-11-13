import { useQueryClient } from '@tanstack/react-query';
import { type ReactNode, useState } from 'react';
import {
    getGetWorldsQueryKey,
    putWorldsCurrent,
    putWorldsPartyIdStopped,
    useGetWorlds,
} from '#api/party-zone';
import type { WorldTile } from '../../api/model';
import { redirectToSignIn } from '../../lib/clerk-session';
import { usePlayerSession, useWorldChoice } from '../../lib/world';
import MoveWorldDialog from './MoveWorldDialog';

const CHOICE =
    'flex w-72 items-center gap-3 rounded-md border-0 bg-transparent p-2 text-left text-lg text-white hover:bg-[#2c5bd3]';

const ADMIN_LINK =
    'border-0 bg-transparent p-0 text-xs text-white underline opacity-80 hover:opacity-100';

interface LoginShellProps {
    prompt: string;
    blurb: string;
    children: ReactNode;
}

/** The XP login frame: greeting on the left, choices on the right. */
export function LoginShell({ prompt, blurb, children }: LoginShellProps) {
    return (
        <div
            className="fixed inset-0 flex flex-col text-white"
            style={{ background: '#5a7edc', fontFamily: 'Tahoma, sans-serif' }}
        >
            <div style={{ height: 80, background: '#00309c' }} />
            <div
                style={{
                    height: 2,
                    background:
                        'linear-gradient(90deg, #00309c, #ffffff 50%, #00309c)',
                }}
            />
            <main className="flex min-h-0 flex-1 items-center justify-center">
                <section className="flex w-1/2 flex-col items-end gap-3 pr-10 text-right">
                    <div className="text-4xl font-bold italic">Partytown</div>
                    <p className="text-lg">{prompt}</p>
                    <p className="max-w-xs text-xs opacity-80">{blurb}</p>
                </section>
                <div
                    className="h-80 w-px"
                    style={{
                        background:
                            'linear-gradient(transparent, #fff, transparent)',
                    }}
                />
                <section className="flex max-h-full w-1/2 flex-col gap-2 overflow-y-auto py-4 pl-10">
                    {children}
                </section>
            </main>
            <div
                style={{
                    height: 2,
                    background:
                        'linear-gradient(90deg, #00309c, #f99736 50%, #00309c)',
                }}
            />
            <footer
                className="px-6 py-4 text-sm"
                style={{ height: 80, background: '#00309c' }}
            >
                After you sign in, the personas may message you on their own.
            </footer>
        </div>
    );
}

/** Current first, then the running ones, the stopped last. */
const inOrder = (worlds: WorldTile[]) =>
    [...worlds].sort(
        (a, b) =>
            Number(b.current) - Number(a.current) ||
            Number(a.stopped) - Number(b.stopped),
    );

const caption = (w: WorldTile) =>
    [
        `${w.people} ${Number(w.people) === 1 ? 'person' : 'people'}`,
        w.stopped ? 'stopped' : null,
    ]
        .filter(Boolean)
        .join(' · ');

/** XP-style login: click a World like a user name, then sign in or look around. Admins stop, switch and move Worlds here. */
export default function WelcomeScreen() {
    const signedIn = usePlayerSession((s) => !!s.userId);
    const visit = usePlayerSession((s) => s.visit);
    const chosen = useWorldChoice((s) => s.id);
    const choose = useWorldChoice((s) => s.choose);
    const queryClient = useQueryClient();
    const { data, isLoading } = useGetWorlds();
    const worlds = inOrder(data?.data.worlds ?? []);
    const isAdmin = data?.data.isAdmin ?? false;
    const [moving, setMoving] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);

    const refresh = () =>
        queryClient.invalidateQueries({ queryKey: getGetWorldsQueryKey() });

    const admin = async (act: () => Promise<unknown>) => {
        setError(null);
        try {
            await act();
            await refresh();
        } catch {
            setError('That did not work. Try again?');
        }
    };

    return (
        <LoginShell
            prompt="To begin, click your World"
            blurb="Everyone who signs in to a World lives in it together. The personas there will remember you."
        >
            {isLoading && <p className="text-sm opacity-80">Looking…</p>}
            <ul className="m-0 flex list-none flex-col gap-3 p-0">
                {worlds.map((w) => {
                    const picked = chosen === w.id;
                    return (
                        <li key={w.id} className="flex flex-col gap-1">
                            <button
                                type="button"
                                onClick={() => choose(w.id)}
                                aria-pressed={picked}
                                className={`${CHOICE} ${picked ? 'bg-[#2c5bd3]' : ''} ${w.stopped ? 'opacity-70' : ''}`}
                            >
                                <span
                                    aria-hidden
                                    className="flex h-12 w-12 shrink-0 items-center justify-center rounded-md border-2 border-white bg-[#6c8fe4] text-2xl"
                                >
                                    {w.stopped ? '🏚️' : '🏙️'}
                                </span>
                                <span className="flex flex-col leading-tight">
                                    <span>{w.name}</span>
                                    <span className="text-xs opacity-80">
                                        {caption(w)}
                                    </span>
                                </span>
                            </button>
                            {picked && !signedIn && (
                                <div className="ml-16 flex gap-3">
                                    <button
                                        type="button"
                                        onClick={redirectToSignIn}
                                    >
                                        Sign in
                                    </button>
                                    <button type="button" onClick={visit}>
                                        Just look around
                                    </button>
                                </div>
                            )}
                            {isAdmin && (
                                <div className="ml-16 flex gap-3">
                                    {!w.current && (
                                        <button
                                            type="button"
                                            className={ADMIN_LINK}
                                            onClick={() =>
                                                admin(() =>
                                                    putWorldsCurrent({
                                                        worldId: w.id,
                                                    }),
                                                )
                                            }
                                        >
                                            Make current
                                        </button>
                                    )}
                                    <button
                                        type="button"
                                        className={ADMIN_LINK}
                                        onClick={() =>
                                            admin(() =>
                                                putWorldsPartyIdStopped(w.id, {
                                                    stopped: !w.stopped,
                                                }),
                                            )
                                        }
                                    >
                                        {w.stopped ? 'Start again' : 'Stop'}
                                    </button>
                                    <button
                                        type="button"
                                        className={ADMIN_LINK}
                                        onClick={() =>
                                            setMoving(
                                                moving === w.id ? null : w.id,
                                            )
                                        }
                                    >
                                        Move everyone to a new World…
                                    </button>
                                </div>
                            )}
                            {moving === w.id && (
                                <MoveWorldDialog
                                    from={w}
                                    onCancel={() => setMoving(null)}
                                    onMoved={async (to) => {
                                        setMoving(null);
                                        await refresh();
                                        choose(to.id);
                                    }}
                                />
                            )}
                        </li>
                    );
                })}
            </ul>
            {error && (
                <p role="alert" className="text-sm">
                    {error}
                </p>
            )}
        </LoginShell>
    );
}
