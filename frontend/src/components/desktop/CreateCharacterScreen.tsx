import { useId, useState } from 'react';
import { useGetWorldsPartyIdPlayerFree } from '#api/party-zone';
import { ProblemDetailsError } from '../../api/custom-fetch';
import {
    useCreatePlayer,
    useTakePersona,
    useWorldChoice,
    WORLD_ID,
} from '../../lib/world';
import Portrait from '../Portrait';
import { LoginShell } from './WelcomeScreen';

const MAX_NAME = 40;

const TILE =
    'flex w-72 items-center gap-3 rounded-md border-0 bg-transparent p-2 text-left text-lg text-white hover:bg-[#2c5bd3]';

const said = (err: unknown) =>
    (err instanceof ProblemDetailsError ? err.detail || err.title : null) ||
    'That did not work. Try again?';

export default function CreateCharacterScreen() {
    const createPlayer = useCreatePlayer();
    const takePersona = useTakePersona();
    const leaveWorld = useWorldChoice((s) => s.leave);
    const { data } = useGetWorldsPartyIdPlayerFree(WORLD_ID);
    const free = data?.data ?? [];
    const nameId = useId();
    const [name, setName] = useState('');
    const [picked, setPicked] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const run = async (act: () => Promise<void>) => {
        setBusy(true);
        setError(null);
        try {
            await act();
        } catch (err) {
            setError(said(err));
            setBusy(false);
        }
    };

    const submit = (e: React.FormEvent) => {
        e.preventDefault();
        void run(() => createPlayer(name.trim()));
    };

    return (
        <LoginShell
            prompt="Who will you be?"
            blurb="Make someone new, or become someone who already lives here and carry on their life."
        >
            <form
                onSubmit={submit}
                className="flex w-72 flex-col gap-2 p-2 text-left"
            >
                <label htmlFor={nameId} className="text-lg">
                    Your name
                </label>
                <div className="flex items-center gap-2">
                    <input
                        id={nameId}
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        maxLength={MAX_NAME}
                        className="flex-1 bg-white text-black"
                        autoComplete="off"
                    />
                    <button type="submit" disabled={busy || !name.trim()}>
                        Go
                    </button>
                </div>
            </form>
            {error && (
                <p role="alert" className="w-72 p-2 text-sm">
                    {error}
                </p>
            )}
            {free.length > 0 && (
                <>
                    <p className="m-0 w-72 p-2 text-sm opacity-80">
                        Or play as someone already here:
                    </p>
                    <ul className="m-0 flex list-none flex-col gap-2 p-0">
                        {free.map((p) => {
                            const chosen = picked === p.id;
                            return (
                                <li key={p.id} className="flex flex-col gap-1">
                                    <button
                                        type="button"
                                        onClick={() =>
                                            setPicked(chosen ? null : p.id)
                                        }
                                        aria-pressed={chosen}
                                        className={`${TILE} ${chosen ? 'bg-[#2c5bd3]' : ''}`}
                                    >
                                        <Portrait
                                            who={p.id}
                                            className="h-12 w-12 shrink-0 rounded-md border-2 border-white object-cover"
                                        />
                                        <span className="flex min-w-0 flex-col leading-tight">
                                            <span>{p.name}</span>
                                            {p.bio && (
                                                <span className="line-clamp-2 text-xs opacity-80">
                                                    {p.bio}
                                                </span>
                                            )}
                                        </span>
                                    </button>
                                    {chosen && (
                                        <div className="ml-16 flex w-56 flex-col gap-1 text-xs">
                                            <span className="opacity-80">
                                                They'll be yours from now on.
                                                Put them on auto to let them
                                                live on their own.
                                            </span>
                                            <button
                                                type="button"
                                                disabled={busy}
                                                className="self-start"
                                                onClick={() =>
                                                    run(() => takePersona(p.id))
                                                }
                                            >
                                                Play as {p.name}
                                            </button>
                                        </div>
                                    )}
                                </li>
                            );
                        })}
                    </ul>
                </>
            )}
            <button
                type="button"
                onClick={leaveWorld}
                className="w-72 border-0 bg-transparent p-2 text-left text-sm text-white underline"
            >
                Pick another World
            </button>
        </LoginShell>
    );
}
