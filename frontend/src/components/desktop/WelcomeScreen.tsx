import {
    CommandButton,
    Hyperlink,
    Icon,
    InfoBar,
    LogonScreen,
    LogonTile,
    ProgressBar,
    Text,
} from '@partytown/ui';
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

interface LoginShellProps {
    prompt: string;
    blurb: string;
    footerStart?: ReactNode;
    children: ReactNode;
}

export function LoginShell({
    prompt,
    blurb,
    footerStart,
    children,
}: LoginShellProps) {
    return (
        <main className="fixed inset-0">
            <LogonScreen
                title="Partytown"
                hint={
                    <>
                        {prompt}
                        <Text tone="muted" className="mt-2 block">
                            {blurb}
                        </Text>
                    </>
                }
                footerStart={footerStart}
                footerEnd="After you sign in, the personas may message you on their own."
            >
                {children}
            </LogonScreen>
        </main>
    );
}

export function LoginError({ children }: { children: ReactNode }) {
    return (
        <InfoBar
            role="alert"
            tone="danger"
            icon={<Icon name="error" />}
            className="max-w-[400px]"
        >
            {children}
        </InfoBar>
    );
}

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
            {isLoading && (
                <ProgressBar
                    marquee
                    label="Looking for Worlds"
                    className="w-[240px]"
                />
            )}
            {worlds.map((w) => {
                const picked = chosen === w.id;
                return (
                    <LogonTile
                        key={w.id}
                        name={w.name}
                        subtitle={caption(w)}
                        picture={
                            <Icon
                                name={w.stopped ? 'ruin' : 'city'}
                                size={48}
                            />
                        }
                        selected={picked}
                        dimmed={!!w.stopped}
                        onClick={() => choose(w.id)}
                        footer={
                            isAdmin ? (
                                <div className="flex flex-wrap gap-x-3 gap-y-1">
                                    {!w.current && (
                                        <Hyperlink
                                            onClick={() =>
                                                admin(() =>
                                                    putWorldsCurrent({
                                                        worldId: w.id,
                                                    }),
                                                )
                                            }
                                        >
                                            Make current
                                        </Hyperlink>
                                    )}
                                    <Hyperlink
                                        onClick={() =>
                                            admin(() =>
                                                putWorldsPartyIdStopped(w.id, {
                                                    stopped: !w.stopped,
                                                }),
                                            )
                                        }
                                    >
                                        {w.stopped ? 'Start again' : 'Stop'}
                                    </Hyperlink>
                                    <Hyperlink
                                        onClick={() =>
                                            setMoving(
                                                moving === w.id ? null : w.id,
                                            )
                                        }
                                    >
                                        Move everyone to a new World…
                                    </Hyperlink>
                                </div>
                            ) : null
                        }
                    >
                        {!signedIn && (
                            <div className="flex flex-wrap gap-2">
                                <CommandButton
                                    isDefault
                                    type="button"
                                    onClick={redirectToSignIn}
                                >
                                    Sign in
                                </CommandButton>
                                <CommandButton onClick={visit}>
                                    Just look around
                                </CommandButton>
                            </div>
                        )}
                    </LogonTile>
                );
            })}
            {error && <LoginError>{error}</LoginError>}
            {moving
                ? (() => {
                      const from = worlds.find((w) => w.id === moving);
                      return from ? (
                          <MoveWorldDialog
                              from={from}
                              onCancel={() => setMoving(null)}
                              onMoved={async (to) => {
                                  setMoving(null);
                                  await refresh();
                                  choose(to.id);
                              }}
                          />
                      ) : null;
                  })()
                : null}
        </LoginShell>
    );
}
