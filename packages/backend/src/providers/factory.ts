import type { LLMProvider } from "@proompting/core/types";
import type { PostHog } from "posthog-node";
import { OllamaLLMProvider } from "./ollama";
import { OpenRouterLLMProvider } from "./openRouter";

/**
 * Create an LLMProvider instance based on environment configuration.
 *
 * @param env - Cloudflare environment containing LLM_PROVIDER and the provider-specific variables:
 *   - when `LLM_PROVIDER` is `"ollama"`: `OLLAMA_BASE_URL` is required
 *   - otherwise: `OPENROUTER_API_KEY` is required
 * @param phClient - PostHog client used for telemetry by the provider
 * @returns An LLMProvider configured for the selected provider (`OllamaLLMProvider` when `env.LLM_PROVIDER` is `"ollama"`, otherwise `OpenRouterLLMProvider`)
 */
export function createLLMProvider(
    env: Cloudflare.Env,
    phClient: PostHog,
): LLMProvider {
    if (env.LLM_PROVIDER === "ollama") {
        return new OllamaLLMProvider(env.OLLAMA_BASE_URL, phClient);
    }
    // Default to OpenRouter
    return new OpenRouterLLMProvider(env.OPENROUTER_API_KEY, phClient);
}