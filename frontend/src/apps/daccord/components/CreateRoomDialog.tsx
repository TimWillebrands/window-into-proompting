import {
    CommandButton,
    Dialog,
    FieldRow,
    Icon,
    Stack,
    Text,
    TextArea,
    TextBox,
} from '@partytown/ui';
import { type FormEvent, useEffect, useId, useRef, useState } from 'react';

interface CreateRoomDialogProps {
    open: boolean;
    creating: boolean;
    errorMessage?: string | null;
    onClose: () => void;
    onSubmit: (name: string, scenario: string) => void;
}

const SCENARIO_MAX = 2000;
const SCENARIO_SOFT_WARN = 1800;

export default function CreateRoomDialog({
    open,
    creating,
    errorMessage,
    onClose,
    onSubmit,
}: CreateRoomDialogProps) {
    const [name, setName] = useState('');
    const [scenario, setScenario] = useState('');
    const formId = useId();
    const nameRef = useRef<HTMLInputElement>(null);

    useEffect(() => {
        if (!open) {
            setName('');
            setScenario('');
        }
    }, [open]);

    if (!open) return null;

    const trimmedName = name.trim();
    const canSubmit = !creating && trimmedName.length > 0;

    const handleSubmit = (e: FormEvent) => {
        e.preventDefault();
        if (!canSubmit) return;
        onSubmit(trimmedName, scenario.trim());
    };

    const close = () => {
        if (!creating) onClose();
    };

    return (
        <Dialog
            title="Create a Room"
            icon={<Icon name="chat-rooms" />}
            contained
            className="w-[440px] max-w-[92%]"
            initialFocus={nameRef}
            onClose={close}
            buttons={
                <>
                    <CommandButton onClick={onClose} disabled={creating}>
                        Cancel
                    </CommandButton>
                    <CommandButton
                        type="submit"
                        form={formId}
                        isDefault
                        disabled={!canSubmit}
                    >
                        {creating ? 'Creating…' : 'Create Room'}
                    </CommandButton>
                </>
            }
        >
            <Stack as="form" id={formId} onSubmit={handleSubmit} gap="lg">
                <FieldRow label="Title" layout="stacked">
                    <TextBox
                        ref={nameRef}
                        value={name}
                        onChange={(e) => setName(e.currentTarget.value)}
                        disabled={creating}
                        maxLength={120}
                        placeholder="e.g. Stealth Horticulture HQ"
                        className="w-full"
                    />
                </FieldRow>

                <FieldRow
                    label="Scenario (optional)"
                    layout="stacked"
                    hint={
                        scenario.length > SCENARIO_SOFT_WARN ? (
                            <Text
                                tone={
                                    scenario.length >= SCENARIO_MAX
                                        ? 'danger'
                                        : 'caution'
                                }
                            >
                                {scenario.length}/{SCENARIO_MAX}
                            </Text>
                        ) : undefined
                    }
                >
                    <TextArea
                        value={scenario}
                        onChange={(e) => setScenario(e.currentTarget.value)}
                        disabled={creating}
                        rows={5}
                        maxLength={SCENARIO_MAX}
                        placeholder="Set the scene — e.g. 'Office of a stealth horticulture startup, late at night.'"
                        className="w-full resize-none"
                    />
                </FieldRow>

                {errorMessage && (
                    <Text as="p" role="alert" tone="danger">
                        {errorMessage}
                    </Text>
                )}
            </Stack>
        </Dialog>
    );
}
