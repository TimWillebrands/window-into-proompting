import { OpenAI } from "@posthog/ai";
import { loadFreeOpenRouterModels } from "../openRouter";
import type {
    LLMGenerationEvent,
    LLMGenerationParams,
    LLMModel,
    LLMProvider,
} from "./types";

export class OpenRouterLLMProvider implements LLMProvider {
    id = "openrouter";
    private ai: OpenAI;

    constructor(apiKey: string, posthogClient?: any) {
        this.ai = new OpenAI({
            baseURL: "https://openrouter.ai/api/v1",
            apiKey: apiKey,
            defaultHeaders: {
                "HTTP-Referer": "https://proompting.party",
                "X-Title": "Proompting Party",
            },
            posthog: posthogClient,
        });
    }

    async getModels(): Promise<LLMModel[]> {
        const data = await loadFreeOpenRouterModels();
        return data.models.map((m) => ({
            id: m.endpoint.model_variant_permaslug,
            name: m.short_name,
            provider: "openrouter",
            description: m.description,
            contextLength: m.context_length,
        }));
    }

    async *generate(
        params: LLMGenerationParams,
    ): AsyncGenerator<LLMGenerationEvent> {
        const controller = new AbortController();
        const timeout = 15000;
        let watchdog: NodeJS.Timeout | undefined;
        let timedout = false;

        const resetWatchdog = () => {
            if (watchdog) clearTimeout(watchdog);
            if (timedout) return;
            watchdog = setTimeout(() => {
                timedout = true;
                console.warn("Generation timed out");
                controller.abort();
            }, timeout);
        };

        try {
            const completion = await this.ai.chat.completions.create(
                {
                    model: params.model,
                    messages: params.messages,
                    stream: true,
                    posthogDistinctId: params.userId,
                    posthogTraceId: params.roomId,
                    posthogProperties: { room_id: params.roomId },
                    posthogGroups: { room_id: params.roomId },
                    response_format: params.responseFormat,
                    temperature: params.temperature, // Passed temperature
                },
                {
                    signal: controller.signal,
                },
            );

            resetWatchdog();

            for await (const chunk of completion) {
                resetWatchdog();
                if (
                    "reasoning" in chunk.choices[0].delta &&
                    typeof chunk.choices[0].delta.reasoning === "string"
                ) {
                    yield {
                        type: "reasoning",
                        data: chunk.choices[0].delta.reasoning,
                    };
                }
                if (typeof chunk.choices[0].delta.content !== "string") {
                    continue;
                }
                if (chunk.choices[0].finish_reason) {
                    yield {
                        type: "error",
                        data: chunk.choices[0].finish_reason,
                    };
                    break;
                }
                yield { type: "message", data: chunk.choices[0].delta.content };
            }
        } catch (err: unknown) {
            console.error("Oh no!", err);
            if (narrowError(err)) {
                if ("message" in err.error) {
                    yield { type: "error", data: `${err?.error?.message}\n` };
                }
                if (err?.error?.metadata?.raw) {
                    yield {
                        type: "error",
                        data: `${err?.error?.metadata?.raw}\n`,
                    };
                }
            }
        } finally {
            timedout = true;
            clearTimeout(watchdog);
        }
    }
}

type GenError = {
    metadata?: {
        raw?: string;
    };
    message?: string;
};

function narrowError(err: unknown): err is { error: GenError } {
    return (
        typeof err === "object" &&
        err !== null &&
        "error" in err &&
        err.error !== null &&
        typeof err.error === "object"
    );
}
