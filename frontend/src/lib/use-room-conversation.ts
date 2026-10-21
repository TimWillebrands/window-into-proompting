import { useEffect, useMemo } from 'react';
import {
    useActiveGenerationPhases,
    useChatGroupGenerationState,
    useChatGroupMessages,
    useRealtimeConnectionStatus,
    useRealtimeStoreActions,
} from '#lib/realtime-store';
import { phaseByPersona } from './room-conversation';

/** One Room as a chat window sees it: holds the party socket open and reads the live state. */
export function useRoomConversation(partyId: string, chatGroupId: string) {
    const { connectPartyRealtime, disconnectPartyRealtime } =
        useRealtimeStoreActions();
    useEffect(() => {
        if (!chatGroupId) return;
        connectPartyRealtime(partyId);
        return () => disconnectPartyRealtime(partyId);
    }, [partyId, chatGroupId, connectPartyRealtime, disconnectPartyRealtime]);

    const messages = useChatGroupMessages(chatGroupId);
    const activeIds = useChatGroupGenerationState(chatGroupId);
    const phases = useActiveGenerationPhases(chatGroupId);
    const connection = useRealtimeConnectionStatus(partyId);
    const byPersona = useMemo(() => phaseByPersona(phases), [phases]);

    return {
        messages,
        activeIds,
        phases,
        phaseByPersona: byPersona,
        connection,
    };
}
