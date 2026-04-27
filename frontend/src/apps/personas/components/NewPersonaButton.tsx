import { useQueryClient } from '@tanstack/react-query';
import { getGetPersonaQueryKey, usePutPersonaId } from '#api/party-zone';

export default function NewPersonaButton({
    onCreated,
}: {
    onCreated: (id: string) => void;
}) {
    const queryClient = useQueryClient();
    const upsertMutation = usePutPersonaId({
        mutation: {
            onSuccess: async (response) => {
                await queryClient.invalidateQueries({
                    queryKey: getGetPersonaQueryKey(),
                });
                const created = response.data;
                if (created?.id) {
                    onCreated(created.id);
                }
            },
        },
    });

    return (
        <button
            type="button"
            className="w-full"
            disabled={upsertMutation.isPending}
            onClick={() => {
                const id = crypto.randomUUID();
                upsertMutation.mutate({
                    id,
                    data: {
                        id,
                        name: 'New Persona',
                        systemPrompt: 'You are a helpful persona.',
                    },
                });
            }}
        >
            {upsertMutation.isPending ? 'Creating...' : 'New Persona'}
        </button>
    );
}
