import { listPersonas } from './persona-mock';
import type { ChatMessage, Party, PersonaMetadata } from './types';

const demoParties: Party[] = [
    {
        id: 'party-1',
        name: 'Launch Planning',
        lastActivity: '2024-03-20T10:00:00.000Z',
        messageCount: 18,
    },
    {
        id: 'party-2',
        name: 'Story Time',
        lastActivity: '2024-03-19T15:30:00.000Z',
        messageCount: 42,
    },
];

const demoMessages: ChatMessage[] = [
    {
        id: 'msg-1',
        partyId: 'party-1',
        sender: 'system',
        body: 'Party channel ready. Invite personas to kick things off.',
        timestamp: 1710928380000,
    },
    {
        id: 'msg-2',
        partyId: 'party-1',
        sender: 'persona',
        personaId: 'persona-1',
        personaName: 'Chill Surfer',
        body: 'Stoked to plan this launch. Wave the release schedule at me.',
        timestamp: 1710928440000,
        status: 'sent',
    },
    {
        id: 'msg-3',
        partyId: 'party-1',
        sender: 'user',
        body: 'Need a rollout plan that highlights our party vibe.',
        timestamp: 1710928560000,
    },
    {
        id: 'msg-4',
        partyId: 'party-1',
        sender: 'persona',
        personaId: 'persona-2',
        personaName: 'Product Ops',
        body: 'Let us split tasks: schedule, hype copy, analytics.',
        timestamp: 1710928620000,
        status: 'sent',
    },
];

let activeMessages = [...demoMessages];

export function listParties(): Party[] {
    return demoParties;
}

export function listPartyMessages(partyId: string): ChatMessage[] {
    return activeMessages.filter((message) => message.partyId === partyId);
}

export function addUserMessage(partyId: string, body: string): ChatMessage {
    const message: ChatMessage = {
        id: crypto.randomUUID(),
        partyId,
        sender: 'user',
        body,
        timestamp: Date.now(),
        status: 'sent',
    };
    activeMessages = [...activeMessages, message];
    return message;
}

export function addMockPersonaResponse(
    partyId: string,
    personaId?: string,
): ChatMessage {
    const personaList = listPersonas();
    const persona =
        personaList.find((entry) => entry.id === personaId) ?? personaList[0];
    const message: ChatMessage = {
        id: crypto.randomUUID(),
        partyId,
        sender: 'persona',
        personaId: persona.id,
        personaName: persona.name,
        body: `Mock response from ${persona.name} at ${new Date().toLocaleTimeString()}.`,
        timestamp: Date.now(),
        status: 'sent',
    };
    activeMessages = [...activeMessages, message];
    return message;
}

export function listPersonaParticipants(): PersonaMetadata[] {
    return listPersonas();
}
