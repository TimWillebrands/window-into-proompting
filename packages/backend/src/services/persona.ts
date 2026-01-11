import type { PersonaMetadata } from "@proompting/core/types";

export async function getAllPersonas(
    env: Cloudflare.Env,
): Promise<PersonaMetadata[]> {
    const personaData = await env.DESKTOP_DATA.list<PersonaMetadata>({
        prefix: "persona:",
    });

    return personaData.keys
        .map((key: { metadata?: PersonaMetadata }) => key.metadata)
        .filter((persona): persona is PersonaMetadata => persona !== undefined);
}
