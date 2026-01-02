import type { OpenAI } from "@posthog/ai";
import type { ChatCompletionMessageParam } from "openai/resources";
import type { Persona } from "@/components/personas";
import type { MessageType } from "../durable_objects/party";
import { cleanJsonString } from "./cleanJson";

type Observer = (chunk: Uint8Array, done: boolean) => void;

export type PartyGeneration =
    | { type: "message"; data: string }
    | { type: "reasoning"; data: string }
    | { type: "error"; data: string }
    | { type: "persona"; data: string };

type CompletionCreateParams = Parameters<
    OpenAI["chat"]["completions"]["create"]
>["0"];

async function* promptLlm(
    ai: OpenAI,
    messages: ChatCompletionMessageParam[],
    model: string,
    userId: string,
    roomId: string,
    personaId: string,
    responseFormat?: CompletionCreateParams["response_format"],
    reasoningEffort?: CompletionCreateParams["reasoning_effort"],
): AsyncGenerator<PartyGeneration> {
    const controller = new AbortController();
    const timeout = 5000;
    let watchdog: NodeJS.Timeout | undefined;
    let timedout = false;

    function resetWatchdog() {
        if (watchdog) clearTimeout(watchdog);
        if (timedout) return;
        watchdog = setTimeout(() => {
            timedout = true;
            console.warn("Generation timed out");
            controller.abort();
        }, timeout);
    }

    try {
        const completion = await ai.chat.completions.create(
            {
                model: model,
                messages: messages,
                stream: true,
                posthogDistinctId: userId,
                posthogTraceId: roomId,
                posthogProperties: { room_id: roomId },
                posthogGroups: { room_id: roomId },
                response_format: responseFormat,
                reasoning_effort: reasoningEffort,
            },
            {
                signal: controller.signal,
            },
        );

        yield { type: "persona", data: personaId };
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
                yield { type: "error", data: `${err?.error?.metadata?.raw}\n` };
            }
        }
    } finally {
        timedout = true;
        clearTimeout(watchdog);
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

const instruction = `<instruction>
    You are a participant in a roleplaying game, you and others are
    acting as colleagues in a team. The chat is happening in a corporate
    slack channel.
    You never acknowledge the game and completely assume your persona.
    Don't output your response wrapped in an xml <message sender="-name-">
    tag like you'll see in the input.
</instruction>`;

export type MessageWithSender = MessageType & {
    senderName: string;
};

export class Generation {
    private readonly ai: OpenAI;
    private readonly history: MessageWithSender[];
    private readonly participants: Persona[];
    private readonly model: string;
    private readonly roomId: string;
    private readonly observers = new Set<Observer>();
    private readonly textEncoder = new TextEncoder();

    private message = "";
    private done = false;

    constructor(
        ai: OpenAI,
        history: MessageWithSender[],
        model: string,
        roomId: string,
        participants: Persona[],
    ) {
        this.ai = ai;
        this.history = history;
        this.participants = participants;
        this.model = model;
        this.roomId = roomId;
    }

    observe(observer: Observer) {
        this.observers.add(observer);
        const chunk = this.toEvent(this.message);
        observer(chunk, this.done);
    }

    async generate(personaOverride: Persona | null, userId: string) {
        var { persona: respondent, overseerMsg } = await this.findRespondent();
        var persona = personaOverride ?? respondent;

        console.log(
            "[generate.ts] Respondent:",
            persona?.id,
            persona?.name,
            overseerMsg,
        );

        if (overseerMsg.stop) {
            this.endConversation();
            return {
                stop: true,
            };
        }

        console.log("[generate.ts] dont stop addicted to the shindig");

        if (persona === undefined || persona === null) {
            throw new Error("No persona found by overseer!");
        }

        this.pushPersonaChange(persona.id);

        const messages: ChatCompletionMessageParam[] = [
            {
                role: "system",
                content: `${instruction}\n${persona.systemPrompt}`,
                name: persona.id,
            },
            ...this.history.map(
                (message) =>
                    ({
                        role:
                            message.senderId === persona?.id
                                ? "assistant"
                                : "user",
                        content:
                            message.senderId === persona?.id
                                ? message.message
                                : `<message sender="${message.senderName}" senderId="${message.senderId}">${message.message}</message>`,
                        name: message.senderId,
                    }) as ChatCompletionMessageParam,
            ),
            // Ya might ask, why not inject the user prompt here? We
            // already have that in the history array. Thats why.
        ];

        const data = promptLlm(
            this.ai,
            messages,
            this.model,
            userId,
            this.roomId,
            persona.id,
        );

        console.log("[generate.ts] Started persona prompt generation");

        for await (const value of data) {
            if (value.type === "message") {
                this.message += value.data;
            }
            const chunk = this.toEvent(value.data, undefined, value.type);
            for (const observer of this.observers) {
                observer(chunk, false);
            }
        }

        return {
            message: this.message,
            persona: persona,
            stop: false,
        };
    }

    private pushPersonaChange(personaId: string) {
        console.log("[generation.ts] Persona changed to:", personaId);
        for (const observer of this.observers) {
            observer(this.toEvent("persona", personaId), false);
        }
    }

    private endConversation() {
        console.log("[generation.ts] Conversation ended");
        for (const observer of this.observers) {
            observer(this.toEvent("stop"), true);
        }
    }

    private async findRespondent() {
        const messages: ChatCompletionMessageParam[] = [
            {
                role: "system",
                content: this.overseerPrompt,
            },
            ...this.history.map(
                (message) =>
                    ({
                        role: "user",
                        content: `<message sender="${message.senderName}">${message.message}</message>`,
                        name: message.senderId,
                    }) as ChatCompletionMessageParam,
            ),
            {
                role: "user",
                content: `Task: assign a persona as follow-up or decide that the conversation is over.
                    Decide based on who is 'interesting' as a persona that has a high level of engagement
                    or relevance to the conversation or as a fun twist to spice up the conversation.
                    Provide a concise reason for your decision. Refer to the persona by their 'senderId'
                    attribute and communicate it as 'personaId' in the response. Also provide a short
                    instruction to the persona on how to engage in the conversation.

                    The response should be a JSON object with the following properties:
                    - personaId: string
                    - reason: string
                    - instruction: string
                    - stop: boolean

                    Example:
                    {
                        "personaId": "123",
                        "reason": "Their sudden interest in this conversation would be hillariously ironic and interesting.",
                        "instruction": "Ask about their history on this subject, mention your own wacky experience with it.",
                        "stop": false
                    }
                    `,
            },
        ];
        const data = promptLlm(
            this.ai,
            messages,
            this.model,
            "overseer",
            this.roomId,
            "overseer",
            {
                type: "json_schema",
                json_schema: {
                    name: "overseer",
                    strict: true,
                    schema: {
                        type: "object",
                        properties: {
                            personaId: {
                                type: "string",
                            },
                            instruction: {
                                type: "string",
                            },
                            reason: {
                                type: "string",
                            },
                            stop: {
                                type: "boolean",
                            },
                        },
                        required: [
                            "reason",
                            "instruction",
                            "personaId",
                            "stop",
                        ],
                    },
                },
            },
            "minimal",
        );

        let overseerMessageStr = "";

        for await (const value of data) {
            if (value.type === "message") {
                overseerMessageStr += value.data;
            }
            const chunk = this.toEvent(value.data, undefined, "overseer");
            for (const observer of this.observers) {
                observer(chunk, false);
            }
        }

        try {
            console.log("Overseer Message:", overseerMessageStr);
            overseerMessageStr = cleanJsonString(overseerMessageStr);
            const overseerMsg = JSON.parse(
                overseerMessageStr,
            ) as OverseerOutput;
            const persona = this.participants.find(
                (p) => p.id === overseerMsg.personaId,
            );

            return { persona, overseerMsg };
        } catch (error) {
            console.error(
                "Error parsing overseer message:\n",
                overseerMessageStr,
                "\n\nerror:\n",
                error,
            );
            throw error;
        }
    }

    //TODO: Currently we just dump all participants their systemprompt into this
    // badboy. Not only is this wasting context but I'm also not sure if this gets
    // us the best response.
    private get overseerPrompt() {
        return `# Instruction
You are the director of a chat room roleplay. Your task is to decide
what persona should be continuing the conversation or that the conversation
is over. Provide a reason for your decision. Make sure to provide a persona
ID in case of a follow-up.

If the conversation has reached it's end make sure the 'stop' property of the
output is set to true. In all other cases set it to false.

# Participants
This is a list of participants in the chat room. Each participant has a unique
ID and a name. In the output you should refer to the participant by their ID.

## Personas
${this.participants
    .map(
        (p) => `
### ${p.name}
**ID: ${p.id}**

${p.systemPrompt}
`,
    )
    .join("\n\n")}
`;
    }

    private toEvent(
        data: string | object,
        id?: string,
        eventType: string = "message",
    ) {
        let message = `id: ${id}\n`;
        if (eventType) {
            message += `event: ${eventType}\n`;
        }
        message += `data: ${JSON.stringify(data)}\n\n`; // Data field followed by double newline
        return this.textEncoder.encode(message);
    }
}

type OverseerOutput = {
    personaId: string;
    instruction: string;
    reason: string;
    stop: boolean;
};
