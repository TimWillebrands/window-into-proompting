import {
    CommandButton,
    GroupBox,
    Icon,
    ListView,
    Stack,
    StatusText,
    Text,
    WindowLayout,
} from '@partytown/ui';
import { useMemo, useState } from 'react';
import { useGetPersonaSuspense } from '#api/party-zone';
import Portrait from '../../components/Portrait';
import GeneratePersonaDialog from './components/GeneratePersonaDialog';
import MoveInDialog from './components/MoveInDialog';
import NewPersonaButton from './components/NewPersonaButton';
import PersonaEditor from './components/PersonaEditor';
import { useResidents } from './residence';

export default function PersonasApp() {
    const personasQuery = useGetPersonaSuspense();
    const personas = useMemo(() => {
        const data = personasQuery.data?.data;
        return Array.isArray(data) ? data : [];
    }, [personasQuery.data]);
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const [showGenerateDialog, setShowGenerateDialog] = useState(false);
    const residents = useResidents();
    const [askingId, setAskingId] = useState<string | null>(null);
    const [askOnSaveId, setAskOnSaveId] = useState<string | null>(null);

    const selectedPersona = useMemo(
        () => personas.find((p) => p.id === selectedId) ?? null,
        [personas, selectedId],
    );
    const asking = personas.find((p) => p.id === askingId);
    const createdComplete = (id: string) => {
        setSelectedId(id);
        setAskingId(id);
    };

    return (
        <>
            <WindowLayout
                taskPaneLabel="Personas"
                taskPane={
                    <Stack gap="md" pad="md">
                        {personasQuery.isError ? (
                            <StatusText tone="danger">
                                Could not load personas. Check that the backend
                                is running, then reopen this window.
                            </StatusText>
                        ) : null}
                        <ListView
                            view="tiles"
                            label={`Personas (${personas.length})`}
                            className="min-h-0 flex-1"
                            selected={selectedId ? [selectedId] : []}
                            onSelectionChange={(ids) =>
                                setSelectedId(ids[0] ?? null)
                            }
                            empty={
                                personasQuery.isError
                                    ? undefined
                                    : 'No personas yet. Create one below — it can then join chat rooms and speak on its own.'
                            }
                            items={personas.map((persona) => ({
                                id: persona.id ?? '',
                                label: persona.name,
                                largeIcon: (
                                    <Portrait
                                        who={persona.id}
                                        alt=""
                                        width={32}
                                        height={32}
                                    />
                                ),
                                details: residents.has(persona.id ?? '')
                                    ? ['In the World']
                                    : undefined,
                            }))}
                        />
                        <GroupBox title="Create a persona">
                            <NewPersonaButton
                                onCreated={(id) => {
                                    setSelectedId(id);
                                    setAskOnSaveId(id);
                                }}
                            />
                            <CommandButton
                                className="w-full"
                                icon={<Icon name="sparkle" />}
                                title="Describe the persona in a sentence and let the AI write the name, bio and system prompt"
                                onClick={() => setShowGenerateDialog(true)}
                            >
                                Generate with AI...
                            </CommandButton>
                        </GroupBox>
                    </Stack>
                }
                contentLabel={selectedPersona?.name ?? 'Personas'}
            >
                {selectedPersona ? (
                    <Stack pad="lg">
                        <PersonaEditor
                            key={selectedPersona.id}
                            persona={selectedPersona}
                            onDeleted={() => setSelectedId(null)}
                            onSaved={() => {
                                if (askOnSaveId !== selectedPersona.id) return;
                                setAskOnSaveId(null);
                                setAskingId(askOnSaveId);
                            }}
                        />
                    </Stack>
                ) : (
                    <Stack align="center" justify="center" pad="lg">
                        <GroupBox
                            title="Personas are the AI characters of this party"
                            icon={<Icon name="personas" />}
                            className="max-w-[340px]"
                        >
                            <Stack direction="row" align="start" gap="lg">
                                <Icon name="users" size={32} />
                                <Text as="p" tone="muted">
                                    Each one has a name, a bio, and a system
                                    prompt that shapes how it speaks in chat
                                    rooms. Select one on the left to edit it, or
                                    create a new one below the list.
                                </Text>
                            </Stack>
                        </GroupBox>
                    </Stack>
                )}
            </WindowLayout>

            {showGenerateDialog && (
                <GeneratePersonaDialog
                    onClose={() => setShowGenerateDialog(false)}
                    onCreated={(id) => {
                        createdComplete(id);
                        setShowGenerateDialog(false);
                    }}
                />
            )}
            {asking && (
                <MoveInDialog
                    persona={asking}
                    onClose={() => setAskingId(null)}
                />
            )}
        </>
    );
}
