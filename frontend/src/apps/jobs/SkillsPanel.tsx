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
import { Card, Empty } from './parts';

/** What one person knows how to do (ADR 0054): who they are (intrinsic) and what doing taught them (acquired). */
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
        <li key={s.id} data-faded={s.rusty || undefined}>
            <span className="wk-grow" title={s.how}>
                {skillLabel(s)}
            </span>
            {isAdmin && !s.intrinsic ? (
                <>
                    <button
                        type="button"
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
                    </button>
                    <button
                        type="button"
                        onClick={() =>
                            forget.mutate({
                                partyId: WORLD_ID,
                                participantId,
                                skillId: s.id,
                            })
                        }
                    >
                        Forget
                    </button>
                </>
            ) : null}
        </li>
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
                <ul className="wk-rows">
                    {intrinsic.map(row)}
                    {acquired.map(row)}
                </ul>
            )}
            {isAdmin ? (
                <form
                    className="wk-inline"
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
                    <input
                        aria-label="What they know how to do"
                        value={skill}
                        placeholder="fixing bikes"
                        onChange={(e) => setSkill(e.currentTarget.value)}
                    />
                    <input
                        className="wk-grow"
                        aria-label="How it goes"
                        value={how}
                        placeholder="you check the tyre before the chain"
                        onChange={(e) => setHow(e.currentTarget.value)}
                    />
                    <button
                        type="submit"
                        disabled={!skill.trim() || add.isPending}
                    >
                        Add
                    </button>
                </form>
            ) : null}
        </Card>
    );
}
