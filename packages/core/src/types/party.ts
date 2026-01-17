import type { PersonaMetadata } from "./persona";

export interface MessageType {
    [key: string]: ArrayBuffer | string | number | null;
    messageid: number;
    message: string | null;
    reasoning: string | null;
    overseer: string | null;
    error: string | null;
    senderType: string;
    senderId: string;
    sendAt: number | null;
    modelEndpointStub: string | null;
}

export type SubscriptionMessage =
    | { type: "join"; messages: MessageType[] }
    | { type: "message"; message: MessageType }
    | { type: "messageStream"; messageId: number }
    | { type: "deleteMessage"; messageId: number }
    | { type: "deleteMessagesAfter"; messageId: number };

export interface PartyInfo {
    id: string;
    name: string;
}

export interface PartyInfoFull extends PartyInfo {
    participants: PersonaMetadata[];
}

export interface Party {
    id: string;
    name: string;
    lastActivity?: string;
    messageCount?: number;
}
