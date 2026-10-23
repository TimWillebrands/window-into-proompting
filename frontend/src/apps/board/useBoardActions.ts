import { useQueryClient } from '@tanstack/react-query';
import {
    getGetWorldsPartyIdBoardQueryKey,
    usePostWorldsPartyIdBoard,
    usePostWorldsPartyIdBoardThreadIdReplies,
    usePostWorldsPartyIdBoardThreadIdRespect,
} from '#api/party-zone';
import type { BoardRespect } from '../../api/model/boardRespect';
import type { BoardThread } from '../../api/model/boardThread';
import { WORLD_ID } from '../../lib/world';

type Cached = { data?: BoardThread[] } | undefined;

/** Post, reply and Respect! for the Player; Respect! flips locally before the server answers. */
export function useBoardActions(
    playerId: string | null,
    playerName: string | null,
) {
    const queryClient = useQueryClient();
    const key = getGetWorldsPartyIdBoardQueryKey(WORLD_ID);
    const refresh = () => queryClient.invalidateQueries({ queryKey: key });

    const postMutation = usePostWorldsPartyIdBoard();
    const replyMutation = usePostWorldsPartyIdBoardThreadIdReplies();
    const respectMutation = usePostWorldsPartyIdBoardThreadIdRespect();

    const post = async (body: string) => {
        const res = await postMutation.mutateAsync({
            partyId: WORLD_ID,
            data: { body, authorId: playerId },
        });
        await refresh();
        return res;
    };

    const reply = async (threadId: string, body: string) => {
        await replyMutation.mutateAsync({
            partyId: WORLD_ID,
            threadId,
            data: { body, authorId: playerId },
        });
        await refresh();
    };

    const respect = async (
        threadId: string,
        replyId: string | null,
        given: boolean,
    ) => {
        if (!playerId) return;
        const flip = (list: BoardRespect[] | undefined) => {
            const rest = (list ?? []).filter((r) => r.giverId !== playerId);
            return given
                ? [
                      ...rest,
                      {
                          giverId: playerId,
                          giverName: playerName ?? 'you',
                          at: Date.now(),
                      },
                  ]
                : rest;
        };
        const previous = queryClient.getQueryData<Cached>(key);
        queryClient.setQueryData<Cached>(key, (old) =>
            old?.data
                ? {
                      ...old,
                      data: old.data.map((t) =>
                          t.id !== threadId
                              ? t
                              : replyId
                                ? {
                                      ...t,
                                      replies: t.replies?.map((r) =>
                                          r.id === replyId
                                              ? {
                                                    ...r,
                                                    respect: flip(r.respect),
                                                }
                                              : r,
                                      ),
                                  }
                                : { ...t, respect: flip(t.respect) },
                      ),
                  }
                : old,
        );
        try {
            await respectMutation.mutateAsync({
                partyId: WORLD_ID,
                threadId,
                data: { replyId, given, authorId: playerId },
            });
        } catch {
            queryClient.setQueryData(key, previous);
        } finally {
            await refresh();
        }
    };

    return { post, reply, respect };
}
