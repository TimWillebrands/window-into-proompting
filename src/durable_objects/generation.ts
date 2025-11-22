import type { OpenAI } from "@posthog/ai";
import type { ChatCompletionMessageParam } from "openai/resources";
import type { Persona } from "@/components/personas";
import type { MessageType } from "./party";

type Observer = (chunk: Uint8Array, done: boolean) => void;

export type PartyGeneration =
    | { type: "msg"; content: string }
    | { type: "error"; message: string }
    | { type: "persona"; id: string };

async function* promptLlm(
    ai: OpenAI,
    messages: ChatCompletionMessageParam[],
    model: string,
    userId: string,
    roomId: string,
    personaId: string,
): AsyncGenerator<PartyGeneration> {
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

        yield { type: "persona", id: personaId };

        for await (const chunk of completion) {
            if (typeof chunk.choices[0].delta.content !== "string") {
                continue;
            }
            yield { type: "msg", content: chunk.choices[0].delta.content };
        }
    } catch (err: any) {
        console.error(err);
        if (err?.error?.message) {
            yield { type: "error", message: `${err?.error?.message}\n` };
        }
        if (err?.error?.metadata?.raw) {
            yield { type: "error", message: `${err?.error?.metadata?.raw}\n` };
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
        const chunk = this.toEvent(this.message);
        observer(chunk, this.done);
    }

    toEvent(data: string, id?: string, eventType: string = "message") {
        let message = `id: ${id}\n`;
        if (eventType) {
            message += `event: ${eventType}\n`;
        }
        message += `data: ${JSON.stringify(data)}\n\n`; // Data field followed by double newline
        return this.textEncoder.encode(message);
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
                                ? message.message
                                : `<message sender="${message.senderName}">${message.message}</message>`,
                        name: message.senderId,
                    }) as ChatCompletionMessageParam,
            ),
            // Ya might ask, why not inject the user prompt here? We
            // already have that in the history array. Thats why.
        ];
        const data = promptLlm(
            this.ai,
            messages,
            model,
            userId,
            roomId,
            persona.id,
        );

        for await (const value of data) {
            if (value.type === "msg") {
                this.message += value.content;
                const chunk = this.toEvent(value.content);
                for (const observer of this.observers) {
                    observer(chunk, false);
                }
            }
        }

        this.done = true;
        for (const observer of this.observers) {
            observer(new Uint8Array(0), true);
        }

        return this.message;
    }
}
