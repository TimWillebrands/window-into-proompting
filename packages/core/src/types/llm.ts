import type { OpenAI } from "@posthog/ai";
import type { ChatCompletionMessageParam } from "openai/resources";

export interface LLMModel {
    id: string;
    name: string;
    description?: string;
    provider: string; // 'openrouter' | 'ollama' etc.
    contextLength?: number;
}

export type LLMGenerationParams = {
    model: string;
    messages: ChatCompletionMessageParam[];
    userId?: string;
    roomId?: string;
    temperature?: number;
    responseFormat?: Parameters<
        OpenAI["chat"]["completions"]["create"]
    >[0]["response_format"];
};

export type LLMGenerationEvent =
    | { type: "message"; data: string }
    | { type: "reasoning"; data: string }
    | { type: "error"; data: string };

export interface LLMProvider {
    id: string;
    getModels(): Promise<LLMModel[]>;
    generate(params: LLMGenerationParams): AsyncGenerator<LLMGenerationEvent>;
}
