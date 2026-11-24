import {
    BuddyList,
    type BuddyListGroup,
    CommandButton,
    Dialog,
    Icon,
    Pane,
    Stack,
    StatusText,
    Text,
    TextBox,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import {
    getGetWorldsPartyIdMessengerQueryKey,
    useGetWorldsPartyIdLivesSuspense,
    useGetWorldsPartyIdSelves,
    usePostWorldsPartyIdMessengerChatGroupIdInvite,
} from '#api/party-zone';
import type { ChatGroupInfo } from '../../api/model/chatGroupInfo';
import type { LifeSummary } from '../../api/model/lifeSummary';
import { notifyOnRefusal, usePlayerId, WORLD_ID } from '../../lib/world';
import { groupOf, MAX_MEMBERS, names, refusal } from './conversations';
import { BuddyIcon, STATUS_LABEL, statusOf } from './msn-ui';

export interface InviteDialogProps {
    chatGroupId: string;
    group: boolean;
    memberIds: readonly string[];
    withName: string;
    onClose: () => void;
    onInvited: (info: ChatGroupInfo, invitee: LifeSummary) => void;
}

export default function InviteDialog({
    chatGroupId,
    group,
    memberIds,
    withName,
    onClose,
    onInvited,
}: InviteDialogProps) {
    const queryClient = useQueryClient();
    const playerId = usePlayerId();
    const lives = useGetWorldsPartyIdLivesSuspense(WORLD_ID).data.data ?? [];
    const selves = useGetWorldsPartyIdSelves(WORLD_ID).data?.data ?? [];
    const selfById = useMemo(
        () => new Map(selves.map((s) => [s.personaId, s])),
        [selves],
    );
    const [filter, setFilter] = useState('');
    const [picked, setPicked] = useState<string[]>([]);
    const [problem, setProblem] = useState<string | null>(null);

    const invite = usePostWorldsPartyIdMessengerChatGroupIdInvite({
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdMessengerQueryKey(WORLD_ID),
                }),
        },
    });

    const inIt = new Set([...memberIds, playerId ?? '']);
    const q = filter.trim().toLowerCase();
    const candidates = lives
        .filter((l) => !inIt.has(l.personaId))
        .filter((l) => !q || l.name.toLowerCase().includes(q))
        .sort(
            (a, b) =>
                Number(a.afk) - Number(b.afk) || a.name.localeCompare(b.name),
        );
    const full = group && memberIds.length >= MAX_MEMBERS;
    const chosen = candidates.find((c) => c.personaId === picked[0]);

    const send = async (who: LifeSummary | undefined) => {
        if (!who || full || invite.isPending) return;
        setProblem(null);
        try {
            const res = await invite.mutateAsync({
                partyId: WORLD_ID,
                chatGroupId,
                data: { personaId: who.personaId, fromPersonaId: playerId },
            });
            onInvited(groupOf(res), who);
        } catch (e) {
            notifyOnRefusal(e);
            setProblem(refusal(e, `Couldn't invite ${who.name}.`));
        }
    };

    const item = (l: LifeSummary) => {
        const status = statusOf(l, selfById.get(l.personaId));
        return {
            id: l.personaId,
            title: `Invite ${l.name}`,
            picture: <BuddyIcon status={status} />,
            name: (
                <>
                    {l.name}
                    {status !== 'online' && (
                        <Text tone="muted"> ({STATUS_LABEL[status]})</Text>
                    )}
                </>
            ),
        };
    };
    const groups: BuddyListGroup[] = [
        {
            id: 'contacts',
            title: 'Contacts',
            items: candidates.map(item),
            empty: q
                ? 'Nobody by that name.'
                : 'Everyone you know is in it already.',
        },
    ];

    return (
        <Dialog
            contained
            skin="msn"
            title="Invite someone"
            icon={<Icon name="users" />}
            onClose={onClose}
            className="w-[320px]"
            buttons={
                <>
                    <CommandButton
                        isDefault
                        type="button"
                        disabled={!chosen || full || invite.isPending}
                        onClick={() => void send(chosen)}
                    >
                        {invite.isPending ? 'Inviting…' : 'Invite'}
                    </CommandButton>
                    <CommandButton
                        type="button"
                        onClick={onClose}
                        disabled={invite.isPending}
                    >
                        Cancel
                    </CommandButton>
                </>
            }
        >
            <Stack gap="sm">
                <Text as="p" tone="muted">
                    {group
                        ? 'Pick someone to add. They can read what was said before.'
                        : `Pick someone to talk with you and ${withName}. That starts a new conversation; what you and ${withName} said stays between you.`}
                </Text>
                <TextBox
                    type="search"
                    value={filter}
                    onChange={(e) => setFilter(e.target.value)}
                    placeholder="Find a contact..."
                    aria-label="Find a contact to invite"
                />
                <Pane surface="sunken" pad="xs" className="h-[180px]">
                    <BuddyList
                        aria-label="Contacts to invite"
                        groups={groups}
                        selected={picked}
                        onSelectionChange={setPicked}
                        onActivate={(id) =>
                            void send(
                                candidates.find((c) => c.personaId === id),
                            )
                        }
                    />
                </Pane>
                {full && (
                    <StatusText tone="caution">
                        A conversation holds at most {MAX_MEMBERS} people.
                    </StatusText>
                )}
                {chosen && !full && (
                    <Text as="p" tone="muted">
                        {group
                            ? `${chosen.name} joins ${names(
                                  lives
                                      .filter(
                                          (l) =>
                                              memberIds.includes(l.personaId) &&
                                              l.personaId !== playerId,
                                      )
                                      .map((l) => l.name),
                              )} and you.`
                            : `A new conversation with ${withName} and ${chosen.name}.`}
                    </Text>
                )}
                {problem && <StatusText tone="danger">{problem}</StatusText>}
            </Stack>
        </Dialog>
    );
}
