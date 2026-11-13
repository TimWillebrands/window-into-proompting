import { useId, useState } from 'react';
import { ProblemDetailsError } from '../../api/custom-fetch';
import { useCreatePlayer, useWorldChoice } from '../../lib/world';
import { LoginShell } from './WelcomeScreen';

const MAX_NAME = 40;

/** First stop for a signed-in human with no Player yet: pick who you'll be here. */
export default function CreateCharacterScreen() {
    const createPlayer = useCreatePlayer();
    const leaveWorld = useWorldChoice((s) => s.leave);
    const nameId = useId();
    const [name, setName] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const submit = async (e: React.FormEvent) => {
        e.preventDefault();
        setBusy(true);
        setError(null);
        try {
            await createPlayer(name.trim());
        } catch (err) {
            const said =
                err instanceof ProblemDetailsError
                    ? err.detail || err.title
                    : null;
            setError(said || 'That did not work. Try again?');
            setBusy(false);
        }
    };

    return (
        <LoginShell
            prompt="Who will you be?"
            blurb="This is the name everyone here will know you by."
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
                {error && (
                    <p role="alert" className="text-sm">
                        {error}
                    </p>
                )}
            </form>
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
