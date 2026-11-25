import {
    CommandButton,
    GroupBox,
    Icon,
    PropertyList,
    Stack,
    StatusText,
    Swatch,
    Text,
} from '@partytown/ui';
import {
    useDeleteLlmConfigProvidersId,
    usePutLlmConfigProvidersId,
} from '#api/party-zone';
import type { LlmProviderEntry } from '../../../api/model/llmProviderEntry';
import type { ProviderAvailability } from '../../../api/model/providerAvailability';
import {
    BENCHED,
    JOB_COMPLEXITIES,
    PROVIDER_STATES,
    providerType,
    REASONING_EFFORTS,
    seriesColor,
} from './constants';

const time = (iso: string) =>
    new Date(iso).toLocaleTimeString([], {
        hour: '2-digit',
        minute: '2-digit',
    });

function AvailabilityBadge({
    availability,
}: {
    availability: ProviderAvailability;
}) {
    const state = PROVIDER_STATES[availability.state] ?? PROVIDER_STATES[0];
    const label =
        availability.state === BENCHED && availability.until
            ? `${state.label} until ${time(availability.until)}`
            : state.label;
    return (
        <StatusText
            as="span"
            tone={state.tone}
            title={availability.reason ?? undefined}
        >
            {label}
        </StatusText>
    );
}

export default function ProviderCard({
    provider,
    providers,
    availability,
    personaCount = 0,
    onEdit,
    onDeleted,
    onToggled,
}: {
    provider: LlmProviderEntry;
    providers?: LlmProviderEntry[];
    availability?: ProviderAvailability;
    personaCount?: number;
    onEdit: () => void;
    onDeleted: () => void;
    onToggled: () => void;
}) {
    const deleteMutation = useDeleteLlmConfigProvidersId({
        mutation: { onSuccess: onDeleted },
    });
    const toggleMutation = usePutLlmConfigProvidersId({
        mutation: { onSuccess: onToggled },
    });

    const complexityLabels = JOB_COMPLEXITIES.filter(
        (c) => ((provider.supportedComplexities ?? 0) & c.value) !== 0,
    ).map((c) => c.label);

    const reasoning = REASONING_EFFORTS.find(
        (r) => r.value !== 0 && r.value === provider.reasoningEffort,
    );

    const type = providerType(provider.type);
    const isEnabled = provider.isEnabled !== false;

    const handleToggle = () => {
        if (provider.id) {
            toggleMutation.mutate({
                id: provider.id,
                data: { ...provider, isEnabled: !isEnabled },
            });
        }
    };

    const handles = provider.reserved
        ? 'Only pinned personas'
        : complexityLabels.join(', ');

    return (
        <GroupBox
            icon={<Icon name={type.icon} />}
            title={
                <Stack as="span" inline direction="row" align="center" gap="sm">
                    {type.label}
                    {providers && (
                        <Swatch colour={seriesColor(providers, provider.id)} />
                    )}
                </Stack>
            }
        >
            <PropertyList
                items={[
                    (!isEnabled || !!availability) && {
                        label: 'Status',
                        value: !isEnabled ? (
                            <StatusText
                                as="span"
                                tone="caution"
                                icon={<Icon name="pause" />}
                            >
                                disabled
                            </StatusText>
                        ) : (
                            availability && (
                                <AvailabilityBadge
                                    availability={availability}
                                />
                            )
                        ),
                    },
                    { label: 'Address', value: provider.baseUrl },
                    !!provider.modelName && {
                        label: 'Model',
                        value: provider.modelName,
                    },
                    !!handles && { label: 'Handles', value: handles },
                    !!reasoning && {
                        label: 'Reasoning',
                        value: reasoning.label,
                    },
                    !!provider.tailnet && {
                        label: 'Tailnet',
                        value: provider.tailnet,
                    },
                    personaCount > 0 && {
                        label: 'Pinned',
                        value: (
                            <StatusText as="span" icon={<Icon name="pin" />}>
                                {personaCount}{' '}
                                {personaCount === 1 ? 'persona' : 'personas'}
                            </StatusText>
                        ),
                    },
                ]}
            />
            {isEnabled && availability?.reason && (
                <Text
                    as="p"
                    tone="muted"
                    className="truncate"
                    title={availability.reason}
                >
                    {availability.reason}
                </Text>
            )}
            <Stack direction="row" justify="end" gap="md">
                <CommandButton
                    size="small"
                    pressed={isEnabled}
                    onClick={handleToggle}
                    title={
                        isEnabled
                            ? 'Click to disable — personas stop using this provider'
                            : 'Click to enable this provider'
                    }
                    disabled={toggleMutation.isPending}
                >
                    {toggleMutation.isPending
                        ? '...'
                        : isEnabled
                          ? 'Enabled'
                          : 'Disabled'}
                </CommandButton>
                <CommandButton size="small" onClick={onEdit}>
                    Edit
                </CommandButton>
                <CommandButton
                    size="small"
                    tone="danger"
                    onClick={() => {
                        if (!provider.id) return;
                        if (
                            !confirm(
                                'Remove this provider? Personas can no longer use it to generate replies.',
                            )
                        )
                            return;
                        deleteMutation.mutate({ id: provider.id });
                    }}
                    disabled={deleteMutation.isPending}
                >
                    {deleteMutation.isPending ? '...' : 'Remove'}
                </CommandButton>
            </Stack>
        </GroupBox>
    );
}
