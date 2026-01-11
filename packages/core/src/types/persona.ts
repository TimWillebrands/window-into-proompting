export interface PersonaMetadata {
    id: string;
    name: string;
    isUser?: boolean;
}

export interface Persona extends PersonaMetadata {
    systemPrompt: string;
}
