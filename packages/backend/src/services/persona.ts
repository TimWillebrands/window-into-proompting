import type { Persona, PersonaMetadata } from "@proompting/core/types";
import type { ChatCompletionMessageParam } from "openai/resources";
import { PostHog } from "posthog-node";
import { createLLMProvider } from "../providers";

const posthogClient = new PostHog(
    "phc_f44OvBqb7P19kNmbDBXlNy4UH8pdoiJcUVKZJ1aN950",
    { host: "https://eu.i.posthog.com" },
);

/**
 * Lists metadata for all personas stored in DESKTOP_DATA.
 *
 * @returns An array of `PersonaMetadata` objects for each persona key; entries without metadata are excluded.
 */
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

/**
 * Ensures a persona has a short descriptive bio and persists it if newly generated.
 *
 * If the persona exists but has no bio (or if `options.force` is true), generates a concise 1–2 sentence bio (under 300 characters) focused on role, tone, and behavioral cues, updates the persona record in storage, and returns the updated persona. If the persona does not exist, returns `null`.
 *
 * @param env - Cloudflare environment bindings used for storage and providers
 * @param personaId - Identifier of the persona to ensure a bio for
 * @param options.force - If true, regenerate and overwrite an existing bio
 * @returns The updated Persona with a non-empty `bio`, or `null` if the persona was not found
 * @throws Error if no LLM models are available for generation
 * @throws Error if the generation produces no content
 */
export async function ensurePersonaBio(
    env: Cloudflare.Env,
    personaId: string,
    options?: { force?: boolean },
): Promise<Persona | null> {
    const persona = await env.DESKTOP_DATA.get<Persona>(
        `persona:${personaId}`,
        {
            type: "json",
        },
    );

    if (!persona) {
        return null;
    }

    if (!options?.force && persona.bio?.trim()) {
        return persona;
    }

    const provider = createLLMProvider(env, posthogClient);
    const models = await provider.getModels();

    if (models.length === 0) {
        throw new Error("No LLM models available for persona bio generation.");
    }

    const messages: ChatCompletionMessageParam[] = [
        {
            role: "system",
            content:
                "You summarize personas for an LLM. Write a concise bio in 1-2 sentences, under 300 characters. Focus on role, tone, and behavioral cues. No bullet points, no quotes.",
        },
        {
            role: "user",
            content: `System prompt:\n${persona.systemPrompt}`,
        },
    ];

    const model = models[0].id;
    const stream = provider.generate({
        model,
        messages,
        temperature: 0.2,
    });

    let bio = "";

    for await (const chunk of stream) {
        if (chunk.type === "message") {
            bio += chunk.data;
        }
    }

    const normalizedBio = bio.replace(/\s+/g, " ").trim();

    if (!normalizedBio) {
        throw new Error("Persona bio generation returned empty content.");
    }

    const updatedPersona: Persona = {
        ...persona,
        bio: normalizedBio,
    };

    await env.DESKTOP_DATA.put(
        `persona:${personaId}`,
        JSON.stringify(updatedPersona),
        {
            metadata: {
                id: updatedPersona.id,
                name: updatedPersona.name,
            },
        },
    );

    return updatedPersona;
}