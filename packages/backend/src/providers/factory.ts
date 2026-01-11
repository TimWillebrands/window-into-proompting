import type { LLMProvider } from "@proompting/core/types";
import type { PostHog } from "posthog-node";
import { OllamaLLMProvider } from "./ollama";
import { OpenRouterLLMProvider } from "./openRouter";

export function createLLMProvider(
    env: Cloudflare.Env,
    phClient?: PostHog,
): LLMProvider {
    if (env.LLM_PROVIDER === "ollama") {
        return new OllamaLLMProvider(env.OLLAMA_BASE_URL, phClient);
    }
    // Default to OpenRouter
    return new OpenRouterLLMProvider(env.OPENROUTER_API_KEY, phClient);
}
