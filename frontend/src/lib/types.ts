export interface Persona {
    id: string;
    name: string;
    systemPrompt: string;
    bio?: string;
}

export interface PersonaMetadata {
    id: string;
    name: string;
    isUser?: boolean;
}

export interface Party {
    id: string;
    name: string;
    lastActivity?: string;
    messageCount?: number;
}

export interface LLMModel {
    id: string;
    name: string;
    description?: string;
    provider: string;
    contextLength?: number;
}

export interface MessageType {
    messageid: number;
    message: string | null;
    reasoning: string | null;
    appraisal: string | null;
    error: string | null;
    senderType: string;
    senderId: string;
    sendAt: number | null;
    modelEndpointStub: string | null;
}

export interface ChatMessage {
    id: string;
    partyId: string;
    sender: 'user' | 'persona' | 'system';
    body: string;
    personaId?: string;
    personaName?: string;
    timestamp: number;
    status?: 'sent' | 'streaming';
}
