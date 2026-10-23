import { type RefObject, useLayoutEffect, useRef, useState } from 'react';
import { avatarUrl, MAX_BODY, remaining } from './board-lib';

interface Props {
    playerId: string | null;
    placeholder: string;
    submitLabel: string;
    /** Resolves when posted; rejects with the reason to show. */
    onSubmit: (body: string) => Promise<unknown>;
    textareaRef?: RefObject<HTMLTextAreaElement | null>;
    compact?: boolean;
    autoFocus?: boolean;
    onCancel?: () => void;
}

/** Wie Wat Waar? box: grows with the text, counts down to 280, Ctrl+Enter posts. */
export default function Composer({
    playerId,
    placeholder,
    submitLabel,
    onSubmit,
    textareaRef,
    compact,
    autoFocus,
    onCancel,
}: Props) {
    const [body, setBody] = useState('');
    const [pending, setPending] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const ownRef = useRef<HTMLTextAreaElement>(null);
    const ref = textareaRef ?? ownRef;
    const left = remaining(body);
    const canSubmit = body.trim().length > 0 && left >= 0 && !pending;

    // biome-ignore lint/correctness/useExhaustiveDependencies: resize on text change only
    useLayoutEffect(() => {
        const el = ref.current;
        if (!el) return;
        el.style.height = 'auto';
        el.style.height = `${el.scrollHeight}px`;
    }, [body]);

    const submit = async () => {
        if (!canSubmit) return;
        setPending(true);
        setError(null);
        try {
            await onSubmit(body.trim());
            setBody('');
        } catch (e) {
            setError(e instanceof Error ? e.message : 'Could not post');
        } finally {
            setPending(false);
        }
    };

    return (
        <div className={`pb-composer ${compact ? 'pb-composer-compact' : ''}`}>
            {playerId && (
                <img
                    className="pb-avatar"
                    src={avatarUrl(playerId, true, compact ? 28 : 40)}
                    alt=""
                    width={compact ? 28 : 40}
                    height={compact ? 28 : 40}
                />
            )}
            <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                <textarea
                    ref={ref}
                    rows={compact ? 1 : 2}
                    value={body}
                    placeholder={placeholder}
                    aria-label={placeholder}
                    // biome-ignore lint/a11y/noAutofocus: opened on purpose by Reply
                    autoFocus={autoFocus}
                    onChange={(e) => setBody(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                            e.preventDefault();
                            void submit();
                        }
                        if (e.key === 'Escape' && onCancel) onCancel();
                    }}
                />
                <div className="flex items-center gap-2">
                    {error && (
                        <span className="pb-error truncate">{error}</span>
                    )}
                    <span className="ml-auto flex items-center gap-2">
                        {body.length > 0 && <CountRing left={left} />}
                        {onCancel && (
                            <button
                                type="button"
                                className="pb-btn"
                                onClick={onCancel}
                            >
                                Cancel
                            </button>
                        )}
                        <button
                            type="button"
                            className="pb-btn pb-btn-primary"
                            disabled={!canSubmit}
                            onClick={() => void submit()}
                            title="Ctrl+Enter"
                        >
                            {pending ? 'Posting…' : submitLabel}
                        </button>
                    </span>
                </div>
            </div>
        </div>
    );
}

/** Twitter-style ring that fills up; shows the number once you're close. */
function CountRing({ left }: { left: number }) {
    const r = 9;
    const circumference = 2 * Math.PI * r;
    const used = Math.min(1, (MAX_BODY - left) / MAX_BODY);
    const tone = left < 0 ? 'over' : left <= 20 ? 'near' : 'ok';
    const stroke = { ok: '#1d5b9e', near: '#f47b20', over: '#b42318' }[tone];
    return (
        <span className={`pb-ring pb-ring-${tone}`}>
            <svg width="22" height="22" viewBox="0 0 22 22" role="img">
                <title>{`${left} characters left`}</title>
                <circle
                    cx="11"
                    cy="11"
                    r={r}
                    fill="none"
                    stroke="#dbe5ef"
                    strokeWidth={2.5}
                />
                <circle
                    cx="11"
                    cy="11"
                    r={r}
                    className="pb-ring-fill"
                    fill="none"
                    stroke={stroke}
                    strokeWidth={2.5}
                    strokeLinecap="round"
                    strokeDasharray={circumference}
                    strokeDashoffset={circumference * (1 - used)}
                />
            </svg>
            {left <= 20 && <span className="pb-ring-num">{left}</span>}
        </span>
    );
}
