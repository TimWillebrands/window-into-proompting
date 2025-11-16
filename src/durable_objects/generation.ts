import type { OpenAI } from "@posthog/ai";
import type { ChatCompletionMessageParam } from "openai/resources";
import type { Persona } from "@/components/personas";
import type { MessageType } from "./party";

type Observer = (chunk: Uint8Array, done: boolean) => void;

async function* promptLlm(
    ai: OpenAI,
    messages: ChatCompletionMessageParam[],
    model: string,
    userId: string,
    roomId: string,
) {
    try {
        console.log("create connection");

        const completion = await ai.chat.completions.create({
            model: model,
            messages: messages,
            stream: true,
            posthogDistinctId: userId,
            posthogProperties: { room_id: roomId },
            posthogGroups: { room_id: roomId },
        });
        console.log("connection created");

        for await (const chunk of completion) {
            yield chunk.choices[0].delta.content;
        }
        console.log("all chunks read");
    } catch (err: any) {
        console.error(err);
        if (err?.error?.message) {
            yield `${err?.error?.message}\n`;
        }
        if (err?.error?.metadata?.raw) {
            yield err?.error?.metadata?.raw;
        }
    }
}

export class Generation {
    private readonly ai: OpenAI;
    private readonly messageId: number;
    private readonly observers = new Set<Observer>();
    private readonly textEncoder = new TextEncoder();

    private message = "";
    private done = false;

    constructor(ai: OpenAI, messageId: number) {
        this.ai = ai;
        this.messageId = messageId;
    }

    observe(observer: Observer) {
        this.observers.add(observer);
        const chunk = this.textEncoder.encode(this.message);
        observer(chunk, this.done);
    }

    async generate(
        history: MessageType[],
        prompt: string,
        model: string,
        persona: Persona,
        userId: string,
        roomId: string,
    ) {
        const messages: ChatCompletionMessageParam[] = [
            {
                role: "system",
                content: `You are part of a multi-person chat, all messages are prefixed with the sender's name.
                You are: ${persona.systemPrompt}`,
                name: persona?.id,
            },
            ...history.map(
                (message) =>
                    ({
                        role:
                            message.senderId === persona.id
                                ? "assistant"
                                : "user",
                        content:
                            message.senderId === persona.id
                                ? message.message
                                : `${message.senderId}: ${message.message}`,
                        name: message.senderId,
                    }) as ChatCompletionMessageParam,
            ),
            { role: "user", content: prompt },
        ];
        const data = promptLlm(this.ai, messages, model, userId, roomId);

        for await (const value of data) {
            if (typeof value !== "string") {
                continue;
            }
            this.message += value;
            const chunk = this.textEncoder.encode(value);
            for (const observer of this.observers) {
                observer(chunk, false);
            }
        }

        this.done = true;
        for (const observer of this.observers) {
            observer(new Uint8Array(0), true);
        }

        return { messageId: this.messageId, message: this.message };
    }
}
