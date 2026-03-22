import type { LLMProvider, MessageType, Persona } from '@proompting/core/types';
import { jsonrepair } from 'jsonrepair';
import type { ChatCompletionMessageParam } from 'openai/resources';

type Observer = (chunk: Uint8Array, done: boolean) => void;

export type PartyGeneration =
    | { type: 'message'; data: string }
    | { type: 'reasoning'; data: string }
    | { type: 'error'; data: string }
    | { type: 'persona'; data: string };

export type EventType =
    | 'message'
    | 'reasoning'
    | 'overseer'
    | 'overseerStop'
    | 'error'
    | 'personaChange'
    | 'userInstruction'
    | 'finished';

const instruction = (scenario: string, personaPrompt: string) => `
# Instruction
You are a participant in a roleplaying game with multiple people in
the chat. You never acknowledge the game and completely assume your persona.
The goal is to create a fun and engaging roleplaying experience for everyone
with interesting dynamics and character development.

# Persona
${personaPrompt}

# Scenario
${scenario}

# Output
Your output may contain
1) **speech** just normal speech you want to say.
2) **thoughts or actions** italicized text within parentheses, refers to your own thoughts, feelings and accompanying actions.

## Output Examples
<example>
_(Erasmus scratches his head in confusion)_

Hey, are you sure about that? I think we should check with the old guy first I think.
</example>
<example>
Guys, no time to catch up I'm afraid.

_(Akira invisibly winks to Sakura in the group)_
_(pants audibly)_
_(Akira unsheathes her sword)_

Lets show them what we've got!!
</example>

Don't output your response wrapped in an xml <message sender="-name-">
tag like you'll see in the input.
`;

export function toUserScopedMessage(message: MessageWithSender): string {
    return `<message sender="${message.senderName}" senderId="${message.senderId}">\n${message.message}\n</message>`;
}

function overseerInstructionMessage(messages: MessageWithSender[]): string {
    return `# Task
Assign a persona as follow-up or decide that the conversation is over.
Decide based on who is 'interesting' as a persona that has a high level of engagement
or relevance to the conversation or as a fun twist to spice up the conversation.
Provide a concise reason for your decision. Refer to the persona by their 'senderId'
attribute and communicate it as 'personaId' in the response. Also provide a short
instruction to the persona on how to engage in the conversation.

## Context
These are the last messages in the conversation to assist in decision making:

${messages.map(toUserScopedMessage).join('\n\n')}

# Output
The response should be a JSON object with the following properties:
- personaId: string
- reason: string
- instruction: string
- stop: boolean

## Examples
Example:
{
    "personaId": "123-abcd-456-efgh",
    "reason": "The sudden interest of Erica in this conversation would be hillariously ironic and interesting because of her earlier feigned disinterest.",
    "instruction": "Ask about their history on this subject, mention your own wacky experience with it.",
    "stop": false
}
`;
}

export type MessageWithSender = MessageType & {
    senderName: string;
};

export class Generation {
    private readonly provider: LLMProvider;
    private readonly history: MessageWithSender[];
    private readonly participants: Persona[];
    private readonly model: string;
    private readonly roomId: string;
    private readonly observers = new Set<Observer>();
    private readonly textEncoder = new TextEncoder();

    private message = '';
    private reasoning = '';
    private done = false;

    constructor(
        provider: LLMProvider,
        history: MessageWithSender[],
        model: string,
        roomId: string,
        participants: Persona[],
    ) {
        this.provider = provider;
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
        try {
            const { persona: respondent, overseerMsg } =
                await this.findRespondent();
            const persona = personaOverride ?? respondent;

            if (persona?.isUser) {
                console.log(
                    '[generate.ts] User selected as respondent, stopping generation.',
                );
                this.pushUserInstruction(overseerMsg.instruction);
                this.endConversation();
                return {
                    stop: true,
                };
            }

            console.log(
                '[generate.ts] Respondent:',
                persona?.id,
                persona?.name,
                overseerMsg,
            );

            if (overseerMsg.stop) {
                this.pushOverseerStop(overseerMsg);
                this.endConversation();
                return {
                    stop: true,
                };
            }

            console.log('[generate.ts] dont stop addicted to the shindig');

            if (persona === undefined || persona === null) {
                const msg = `No persona ${overseerMsg.personaId} found by overseer!`;
                this.pushError(msg);
                this.endConversation();
                return {
                    stop: true,
                };
            }

            this.pushPersonaChange(persona.id);

            const messages: ChatCompletionMessageParam[] = [
                {
                    role: 'system',
                    content: instruction(
                        'The headquarters of a stealth software startup producing a mobile app for the horticulture industry',
                        persona.systemPrompt,
                    ),
                    name: persona.id,
                },
                ...this.history.map(
                    (message) =>
                        ({
                            role:
                                message.senderId === persona?.id
                                    ? 'assistant'
                                    : 'user',
                            content:
                                message.senderId === persona?.id
                                    ? message.message
                                    : toUserScopedMessage(message),
                            name: message.senderId,
                        }) as ChatCompletionMessageParam,
                ),
                // Ya might ask, why not inject the user prompt here? We
                // already have that in the history array. Thats why.
            ];

            const data = this.provider.generate({
                model: this.model,
                messages: messages,
                userId: userId,
                roomId: this.roomId,
            });

            console.log('[generate.ts] Started persona prompt generation');

            for await (const value of data) {
                if (value.type === 'message') {
                    this.message += value.data;
                } else if (value.type === 'reasoning') {
                    this.reasoning += value.data;
                }
                const chunk = this.toEvent(value.data, undefined, value.type);
                for (const observer of this.observers) {
                    observer(chunk, false);
                }
            }

            return {
                message: this.message,
                reasoning: this.reasoning,
                overseer: overseerMsg,
                persona: persona,
                stop: false,
            };
        } catch (error) {
            console.error('[generate.ts] Error during generation:', error);
            this.pushError(
                error instanceof Error ? error.message : 'Unknown error',
            );
            this.endConversation();
            return {
                stop: true,
            };
        }
    }

    private pushPersonaChange(personaId: string) {
        console.log('[generation.ts] Persona changed to:', personaId);
        for (const observer of this.observers) {
            observer(
                this.toEvent(personaId, personaId, 'personaChange'),
                false,
            );
        }
    }

    private pushOverseerStop(overseerMsg: OverseerOutput) {
        console.log('[generation.ts] Overseer stop:', overseerMsg);
        for (const observer of this.observers) {
            observer(
                this.toEvent(
                    {
                        reason: overseerMsg.reason,
                        instruction: overseerMsg.instruction,
                    },
                    undefined,
                    'overseerStop',
                ),
                false,
            );
        }
    }

    private pushUserInstruction(instruction: string) {
        console.log('[generation.ts] Pushing user instruction:', instruction);
        for (const observer of this.observers) {
            observer(
                this.toEvent(instruction, undefined, 'userInstruction'),
                false,
            );
        }
    }

    private pushError(errorMessage: string) {
        for (const observer of this.observers) {
            observer(this.toEvent(errorMessage, undefined, 'error'), false);
        }
    }

    private endConversation() {
        console.log('[generation.ts] Conversation ended');
        for (const observer of this.observers) {
            observer(this.toEvent('finished', undefined, 'finished'), true);
        }
    }

    private async findRespondent() {
        const messages: ChatCompletionMessageParam[] = [
            {
                role: 'system',
                content: this.overseerPrompt,
            },
            {
                role: 'user',
                content: overseerInstructionMessage(this.history.slice(-4)),
            },
        ];
        const data = this.provider.generate({
            model: this.model,
            messages: messages,
            userId: 'overseer',
            roomId: this.roomId,
            responseFormat: {
                type: 'json_schema',
                json_schema: {
                    name: 'overseer',
                    strict: true,
                    schema: {
                        type: 'object',
                        properties: {
                            personaId: {
                                type: 'string',
                            },
                            instruction: {
                                type: 'string',
                            },
                            reason: {
                                type: 'string',
                            },
                            stop: {
                                type: 'boolean',
                            },
                        },
                        required: [
                            'reason',
                            'instruction',
                            'personaId',
                            'stop',
                        ],
                    },
                },
            },
            // Note: reasoning_effort is not yet supported in the common interface,
            // but we can add it if needed. For now assuming "minimal" is handled or ignored.
        });

        let overseerMessageStr = '';

        for await (const value of data) {
            if (value.type === 'message') {
                overseerMessageStr += value.data;
            }
            const chunk = this.toEvent(value.data, undefined, 'overseer');
            for (const observer of this.observers) {
                observer(chunk, false);
            }
        }

        try {
            console.log('Overseer Message:', overseerMessageStr);
            overseerMessageStr = jsonrepair(overseerMessageStr);
            const overseerMsg = JSON.parse(
                overseerMessageStr,
            ) as OverseerOutput;
            const persona = this.participants.find(
                (p) => p.id === overseerMsg.personaId,
            );

            // TODO: If persona is undefined retry a limited number of times

            return { persona, overseerMsg };
        } catch (error) {
            console.error(
                'Error parsing overseer message:\n',
                overseerMessageStr,
                '\n\nerror:\n',
                error,
            );
            throw error;
        }
    }

    private personaDetails(persona: Persona) {
        return `
### ${persona.name}
<persona id="${persona.id}" name="${persona.name}">
${persona.bio}
</persona>
`;
    }

    //TODO: Currently we just dump all participants their systemprompt into this
    // badboy. Not only is this wasting context but I'm also not sure if this gets
    // us the best response.
    private get overseerPrompt() {
        return `
# Instruction
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
${this.participants.map(this.personaDetails).join('\n\n')}
`;
    }

    private toEvent(
        data: string | object,
        id?: string,
        eventType: EventType | undefined = 'message',
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
