import { CommandButton, TextBox } from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import {
    getGetWorldsPartyIdSkillsQueryKey,
    useDeleteWorldsPartyIdSkillsParticipantIdSkillId,
    useGetWorldsPartyIdSkillsSuspense,
    usePostWorldsPartyIdSkillsParticipantId,
    usePostWorldsPartyIdSkillsParticipantIdSkillIdPromote,
} from '#api/party-zone';
import type { SkillView } from '../../api/model';
import { useIsAdmin, WORLD_ID } from '../../lib/world';
import { skillLabel } from './jobs-lib';
import { Card, Empty, Row, Rows } from './parts';

export function SkillsPanel({
    participantId,
    name,
}: {
    participantId: string;
    name: string;
}) {
    const queryClient = useQueryClient();
    const isAdmin = useIsAdmin();
    const all = useGetWorldsPartyIdSkillsSuspense(WORLD_ID).data.data ?? [];
    const mine = all.find((s) => s.participantId === participantId);
    const [skill, setSkill] = useState('');
    const [how, setHow] = useState('');
    const refresh = {
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdSkillsQueryKey(WORLD_ID),
                }),
        },
    };
    const add = usePostWorldsPartyIdSkillsParticipantId(refresh);
    const forget = useDeleteWorldsPartyIdSkillsParticipantIdSkillId(refresh);
    const promote =
        usePostWorldsPartyIdSkillsParticipantIdSkillIdPromote(refresh);

    const row = (s: SkillView) => (
        <Row key={s.id}>
            <span className="min-w-0 flex-1" title={s.how}>
                {skillLabel(s)}
            </span>
            {isAdmin && !s.intrinsic ? (
                <>
                    <CommandButton
                        size="small"
                        title="Make this part of who they are, in every world"
                        onClick={() =>
                            promote.mutate({
                                partyId: WORLD_ID,
                                participantId,
                                skillId: s.id,
                            })
                        }
                    >
                        Promote
                    </CommandButton>
                    <CommandButton
                        size="small"
                        tone="danger"
                        onClick={() =>
                            forget.mutate({
                                partyId: WORLD_ID,
                                participantId,
                                skillId: s.id,
                            })
                        }
                    >
                        Forget
                    </CommandButton>
                </>
            ) : null}
        </Row>
    );

    const intrinsic = mine?.intrinsic ?? [];
    const acquired = mine?.acquired ?? [];
    return (
        <Card title={`What ${name} knows how to do`}>
            {intrinsic.length + acquired.length === 0 ? (
                <Empty>
                    Nothing yet. Personas read theirs off who they are, and pick
                    up more by doing the same thing a few times.
                </Empty>
            ) : (
                <Rows>
                    {intrinsic.map(row)}
                    {acquired.map(row)}
                </Rows>
            )}
            {isAdmin ? (
                <form
                    className="flex items-center gap-1.5"
                    onSubmit={(e) => {
                        e.preventDefault();
                        if (!skill.trim()) return;
                        add.mutate(
                            {
                                partyId: WORLD_ID,
                                participantId,
                                data: {
                                    name: skill.trim(),
                                    how: how.trim() || null,
                                },
                            },
                            {
                                onSuccess: () => {
                                    setSkill('');
                                    setHow('');
                                },
                            },
                        );
                    }}
                >
                    <TextBox
                        aria-label="What they know how to do"
                        value={skill}
                        placeholder="fixing bikes"
                        onChange={(e) => setSkill(e.currentTarget.value)}
                    />
                    <TextBox
                        className="min-w-0 flex-1"
                        aria-label="How it goes"
                        value={how}
                        placeholder="you check the tyre before the chain"
                        onChange={(e) => setHow(e.currentTarget.value)}
                    />
                    <CommandButton
                        type="submit"
                        isDefault
                        disabled={!skill.trim() || add.isPending}
                    >
                        Add
                    </CommandButton>
                </form>
            ) : null}
        </Card>
    );
}
