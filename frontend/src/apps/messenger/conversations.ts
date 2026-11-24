import { ProblemDetailsError } from '../../api/custom-fetch';
import type { ChatGroupInfo } from '../../api/model/chatGroupInfo';
import type { ConversationMember } from '../../api/model/conversationMember';
import type { DirectChatSummary } from '../../api/model/directChatSummary';

export const MAX_MEMBERS = 8;

export const isGroup = (c: Pick<DirectChatSummary, 'kind'>) =>
    c.kind === 'room';

export const othersIn = (
    c: Pick<DirectChatSummary, 'members'>,
    playerId: string | null,
): ConversationMember[] => (c.members ?? []).filter((m) => m.id !== playerId);

export function names(list: readonly string[]): string {
    if (list.length === 0) return 'nobody';
    if (list.length === 1) return list[0];
    if (list.length <= 4)
        return `${list.slice(0, -1).join(', ')} and ${list[list.length - 1]}`;
    return `${list.slice(0, 3).join(', ')} and ${list.length - 3} others`;
}

export const hasOwnName = (
    c: Pick<DirectChatSummary, 'name' | 'members'>,
): boolean =>
    !!c.name?.trim() && !(c.members ?? []).some((m) => c.name.includes(m.name));

export function conversationName(
    c: DirectChatSummary,
    playerId: string | null,
): string {
    if (!isGroup(c)) return c.withName;
    if (hasOwnName(c)) return c.name;
    return names(othersIn(c, playerId).map((m) => m.name));
}

export function conversationTitle(
    c: DirectChatSummary,
    playerId: string | null,
): string {
    if (!isGroup(c)) return `${c.withName} — Conversation`;
    if (hasOwnName(c)) return `${c.name} — Conversation`;
    return `Conversation with ${othersIn(c, playerId)
        .map((m) => m.name)
        .join(', ')}`;
}

export const groupTitle = (others: readonly string[]) =>
    `Conversation with ${others.join(', ')}`;

export function refusal(error: unknown, fallback: string): string {
    if (error instanceof ProblemDetailsError)
        return error.detail?.trim() || error.title?.trim() || fallback;
    return fallback;
}

export const groupOf = (res: { data: unknown }): ChatGroupInfo =>
    (res.data ?? {}) as ChatGroupInfo;
