import { CommandButton, Icon, ProgressBar, TextArea } from '@partytown/ui';
import { type RefObject, useLayoutEffect, useRef, useState } from 'react';
import Portrait from '../../components/Portrait';
import { MAX_BODY, remaining } from './board-lib';

interface Props {
    playerId: string | null;
    placeholder: string;
    submitLabel: string;
    onSubmit: (body: string) => Promise<unknown>;
    textareaRef?: RefObject<HTMLTextAreaElement | null>;
    compact?: boolean;
    autoFocus?: boolean;
    onCancel?: () => void;
}

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
        <div className={compact ? 'flex gap-2 pt-1.5' : 'flex gap-3'}>
            {playerId && (
                <Portrait
                    className="flex-none self-start"
                    who={playerId}
                    alt=""
                    width={compact ? 28 : 40}
                    height={compact ? 28 : 40}
                />
            )}
            <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                <TextArea
                    ref={ref}
                    className="max-h-[220px] resize-none"
                    rows={compact ? 1 : 2}
                    value={body}
                    placeholder={placeholder}
                    aria-label={placeholder}
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
                        <span className="flex min-w-0 items-center gap-1">
                            <Icon name="error" />
                            <span className="truncate">{error}</span>
                        </span>
                    )}
                    <span className="ml-auto flex items-center gap-2">
                        {body.length > 0 && <Count left={left} />}
                        {onCancel && (
                            <CommandButton size="small" onClick={onCancel}>
                                Cancel
                            </CommandButton>
                        )}
                        <CommandButton
                            type="button"
                            size="small"
                            isDefault
                            disabled={!canSubmit}
                            onClick={() => void submit()}
                            title="Ctrl+Enter"
                        >
                            {pending ? 'Posting…' : submitLabel}
                        </CommandButton>
                    </span>
                </div>
            </div>
        </div>
    );
}

function Count({ left }: { left: number }) {
    return (
        <span className="flex items-center gap-1">
            <ProgressBar
                className="w-[64px]"
                value={MAX_BODY - left}
                max={MAX_BODY}
                label="Characters used"
                valueText={`${left} characters left`}
            />
            {left <= 20 && <span>{left}</span>}
        </span>
    );
}
