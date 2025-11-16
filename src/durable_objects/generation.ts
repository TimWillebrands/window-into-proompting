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
        const completion = await ai.chat.completions.create({
            model: model,
            messages: messages,
            stream: true,
            posthogDistinctId: userId,
            posthogTraceId: roomId,
            posthogProperties: { room_id: roomId },
            posthogGroups: { room_id: roomId },
        });

        for await (const chunk of completion) {
            yield chunk.choices[0].delta.content;
        }
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

const instruction = `<instruction>
    You are a participant in a roleplaying game, you and others are
    acting as colleagues in a team. The chat is happening in a corporate
    slack channel.
    You never acknowledge the game and completely assume your persona.
    Don't output your response wrapped in an xml <message sender="-name-">
    tag like you'll see in the input.
</instruction>`;

export class Generation {
    private readonly ai: OpenAI;
    private readonly observers = new Set<Observer>();
    private readonly textEncoder = new TextEncoder();

    private message = "";
    private done = false;

    constructor(ai: OpenAI) {
        this.ai = ai;
    }

    observe(observer: Observer) {
        this.observers.add(observer);
        const chunk = this.textEncoder.encode(this.message);
        observer(chunk, this.done);
    }

    async generate(
        history: (MessageType & { senderName: string })[],
        model: string,
        persona: Persona,
        userId: string,
        roomId: string,
    ) {
        const messages: ChatCompletionMessageParam[] = [
            {
                role: "system",
                content: `${instruction}\n${persona.systemPrompt}`,
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
                                ? `<message sender="you">${message.message}</message>`
                                : `<message sender="${message.senderName}">${message.message}</message>`,
                        name: message.senderId,
                    }) as ChatCompletionMessageParam,
            ),
            // Ya might ask, why not inject the user prompt here? We
            // already have that in the history array. Thats why.
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

        return this.message;
    }
}
