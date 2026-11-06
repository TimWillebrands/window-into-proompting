import { useQueryClient } from '@tanstack/react-query';
import {
    getGetWorldsPartyIdJobsQueryKey,
    getGetWorldsPartyIdLivesQueryKey,
    useDeleteWorldsPartyIdResidentsPersonaId,
    useGetWorldsPartyIdLivesSuspense,
    usePostWorldsPartyIdResidents,
} from '#api/party-zone';
import type { Persona } from '../../api/model';
import { WORLD_ID } from '../../lib/world';

const NARRATOR_ID = '0000aaaa-0000-0000-0000-00000000a17e';
const HUMAN_PROMPT = 'A human visiting this world.';

/** Narrator is always there; humans arrive as Players. */
export function canMoveIn(persona: Persona): boolean {
    return persona.id !== NARRATOR_ID && persona.systemPrompt !== HUMAN_PROMPT;
}

/** Ids of everyone in the World's cast. */
export function useResidents(): Set<string> {
    const lives = useGetWorldsPartyIdLivesSuspense(WORLD_ID).data.data ?? [];
    return new Set(lives.map((l) => l.personaId));
}

/** Moves Personas in and out; Lives and the hire list follow. */
export function useResidency() {
    const queryClient = useQueryClient();
    const refresh = () =>
        Promise.all(
            [
                getGetWorldsPartyIdLivesQueryKey(WORLD_ID),
                getGetWorldsPartyIdJobsQueryKey(WORLD_ID),
            ].map((queryKey) => queryClient.invalidateQueries({ queryKey })),
        );
    const moveIn = usePostWorldsPartyIdResidents({
        mutation: { onSuccess: refresh },
    });
    const moveOut = useDeleteWorldsPartyIdResidentsPersonaId({
        mutation: { onSuccess: refresh },
    });
    const error = moveIn.error ?? moveOut.error;
    return {
        moveIn: (personaId: string, onDone?: () => void) =>
            moveIn.mutate(
                { partyId: WORLD_ID, data: { personaId } },
                { onSuccess: () => onDone?.() },
            ),
        moveOut: (personaId: string) =>
            moveOut.mutate({ partyId: WORLD_ID, personaId }),
        isPending: moveIn.isPending || moveOut.isPending,
        error: error
            ? error instanceof Error
                ? error.message
                : 'That did not work.'
            : null,
    };
}
