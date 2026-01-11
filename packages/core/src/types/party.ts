import type { PersonaMetadata } from "./persona";

export interface MessageType {
    messageid: number;
    message?: string;
    reasoning?: string;
    overseer?: string;
    senderType: string;
    senderId: string;
    sendAt?: number;
    modelEndpointStub?: string;
}

export type SubscriptionMessage =
    | { type: "join"; messages: MessageType[] }
    | { type: "message"; message: MessageType }
    | { type: "messageStream"; messageId: number };

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
