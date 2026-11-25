import {
    CommandButton,
    Icon,
    LogonAction,
    LogonTile,
    Text,
    TextBox,
} from '@partytown/ui';
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
import { LoginError, LoginShell } from './WelcomeScreen';

const MAX_NAME = 40;
const NEW = 'new';

const said = (err: unknown) =>
    (err instanceof ProblemDetailsError ? err.detail || err.title : null) ||
    'That did not work. Try again?';

export default function CreateCharacterScreen() {
    const createPlayer = useCreatePlayer();
    const takePersona = useTakePersona();
    const leaveWorld = useWorldChoice((s) => s.leave);
    const { data } = useGetWorldsPartyIdPlayerFree(WORLD_ID);
    const free = data?.data ?? [];
    const [name, setName] = useState('');
    const [picked, setPicked] = useState<string>(NEW);
    const nameId = useId();
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
            footerStart={
                <LogonAction
                    icon={<Icon name="back" size={32} />}
                    onClick={leaveWorld}
                >
                    Pick another World
                </LogonAction>
            }
        >
            <LogonTile
                name="Someone new"
                subtitle="Start a life of your own"
                picture={<Icon name="new" size={48} />}
                selected={picked === NEW}
                onClick={() => setPicked(NEW)}
            >
                <form onSubmit={submit} className="flex flex-col gap-1">
                    <label htmlFor={nameId}>Type your name</label>
                    <div className="flex items-center gap-2">
                        <TextBox
                            id={nameId}
                            value={name}
                            onChange={(e) => setName(e.target.value)}
                            maxLength={MAX_NAME}
                            className="w-[200px]"
                            autoComplete="off"
                        />
                        <CommandButton
                            isDefault
                            disabled={busy || !name.trim()}
                        >
                            Go
                        </CommandButton>
                    </div>
                </form>
            </LogonTile>
            {free.map((p) => (
                <LogonTile
                    key={p.id}
                    name={p.name}
                    subtitle={
                        p.bio ? (
                            <span className="line-clamp-2 whitespace-normal">
                                {p.bio}
                            </span>
                        ) : undefined
                    }
                    picture={<Portrait who={p.id} size={48} />}
                    selected={picked === p.id}
                    onClick={() => setPicked(p.id ?? NEW)}
                >
                    <Text as="p" tone="muted">
                        They'll be yours from now on. Put them on auto to let
                        them live on their own.
                    </Text>
                    <CommandButton
                        disabled={busy}
                        onClick={() => run(() => takePersona(p.id))}
                    >
                        Play as {p.name}
                    </CommandButton>
                </LogonTile>
            ))}
            {error && <LoginError>{error}</LoginError>}
        </LoginShell>
    );
}
