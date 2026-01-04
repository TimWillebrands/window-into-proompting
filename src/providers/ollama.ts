import { OpenAI } from "@posthog/ai";
import type {
    LLMGenerationEvent,
    LLMGenerationParams,
    LLMModel,
    LLMProvider,
} from "./types";

export class OllamaLLMProvider implements LLMProvider {
    id = "ollama";
    private ai: OpenAI;

    constructor(baseUrl: string, posthogClient?: any) {
        this.ai = new OpenAI({
            baseURL: `${baseUrl}/v1`,
            apiKey: "ollama", // Required but ignored by Ollama
            posthog: posthogClient,
        });
    }

    async getModels(): Promise<LLMModel[]> {
        try {
            const list = await this.ai.models.list();
            return list.data.map((m) => ({
                id: m.id,
                name: m.id,
                provider: "ollama",
                description: `Ollama model ${m.id}`,
            }));
        } catch (e) {
            console.error("Error fetching ollama models", e);
            return [];
        }
    }

    async *generate(
        params: LLMGenerationParams,
    ): AsyncGenerator<LLMGenerationEvent> {
        const controller = new AbortController();
        const timeout = 60000;
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
                    temperature: params.temperature,
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
                    // Ollama might send finish_reason="stop" or similar
                    // We might not want to treat all finish_reasons as errors, but for now follow existing pattern
                    if (chunk.choices[0].finish_reason !== "stop") {
                        yield {
                            type: "error",
                            data: chunk.choices[0].finish_reason,
                        };
                    }
                    break;
                }
                yield { type: "message", data: chunk.choices[0].delta.content };
            }
        } catch (err: unknown) {
            console.error("Ollama generation error", err);
            // Re-use logic to narrow error if possible, or just yield generic error
            yield { type: "error", data: String(err) };
        } finally {
            timedout = true;
            clearTimeout(watchdog);
        }
    }
}
