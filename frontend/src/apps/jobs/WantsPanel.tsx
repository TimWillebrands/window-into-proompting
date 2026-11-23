import { ComboBox, CommandButton, TextBox } from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import {
    getGetWorldsPartyIdDesiresQueryKey,
    useGetWorldsPartyIdDesiresSuspense,
    usePostWorldsPartyIdDesiresParticipantId,
    usePostWorldsPartyIdDesiresParticipantIdDesireIdPromote,
    usePostWorldsPartyIdDesiresParticipantIdDesireIdStatus,
} from '#api/party-zone';
import type { Desire, DesireKind } from '../../api/model';
import { WORLD_ID } from '../../lib/world';
import { desireLabel } from './jobs-lib';
import { Card, Empty, Row, Rows } from './parts';

export function WantsPanel({
    participantId,
    name,
    nameOf,
}: {
    participantId: string;
    name: string;
    nameOf: (id: string) => string;
}) {
    const queryClient = useQueryClient();
    const all = useGetWorldsPartyIdDesiresSuspense(WORLD_ID).data.data ?? [];
    const mine = all.find((d) => d.participantId === participantId);
    const [text, setText] = useState('');
    const [kind, setKind] = useState<DesireKind>('Being');
    const refresh = {
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdDesiresQueryKey(WORLD_ID),
                }),
        },
    };
    const add = usePostWorldsPartyIdDesiresParticipantId(refresh);
    const mark =
        usePostWorldsPartyIdDesiresParticipantIdDesireIdStatus(refresh);
    const promote =
        usePostWorldsPartyIdDesiresParticipantIdDesireIdPromote(refresh);

    const row = (d: Desire, acquired: boolean) => (
        <Row key={d.id}>
            <span className="min-w-0 flex-1">{desireLabel(d, nameOf)}</span>
            {d.status === 'Active' ? (
                <>
                    <CommandButton
                        size="small"
                        tone="go"
                        onClick={() =>
                            mark.mutate({
                                partyId: WORLD_ID,
                                participantId,
                                desireId: d.id ?? '',
                                data: { status: 'Fulfilled' },
                            })
                        }
                    >
                        Got it
                    </CommandButton>
                    <CommandButton
                        size="small"
                        tone="caution"
                        onClick={() =>
                            mark.mutate({
                                partyId: WORLD_ID,
                                participantId,
                                desireId: d.id ?? '',
                                data: { status: 'Abandoned' },
                            })
                        }
                    >
                        Drop
                    </CommandButton>
                </>
            ) : null}
            {acquired ? (
                <CommandButton
                    size="small"
                    title="Make this part of who they are, in every world"
                    onClick={() =>
                        promote.mutate({
                            partyId: WORLD_ID,
                            participantId,
                            desireId: d.id ?? '',
                        })
                    }
                >
                    Promote
                </CommandButton>
            ) : null}
        </Row>
    );

    const intrinsic = mine?.intrinsic ?? [];
    const acquired = mine?.acquired ?? [];
    return (
        <Card title={`What ${name} wants`}>
            {intrinsic.length + acquired.length === 0 ? (
                <Empty>
                    Nothing yet. Personas read theirs off who they are; it can
                    take a moment.
                </Empty>
            ) : (
                <Rows>
                    {intrinsic.map((d) => row(d, false))}
                    {acquired.map((d) => row(d, true))}
                </Rows>
            )}
            <form
                className="flex items-center gap-1.5"
                onSubmit={(e) => {
                    e.preventDefault();
                    if (!text.trim()) return;
                    add.mutate(
                        {
                            partyId: WORLD_ID,
                            participantId,
                            data: { kind, text: text.trim() },
                        },
                        { onSuccess: () => setText('') },
                    );
                }}
            >
                <ComboBox
                    aria-label="Kind"
                    value={kind}
                    onChange={(e) =>
                        setKind(e.currentTarget.value as DesireKind)
                    }
                >
                    <option value="Having">to have</option>
                    <option value="Being">to become</option>
                </ComboBox>
                <TextBox
                    className="min-w-0 flex-1"
                    aria-label="What they want"
                    value={text}
                    placeholder="a navy hoodie, get better at chess"
                    onChange={(e) => setText(e.currentTarget.value)}
                />
                <CommandButton
                    type="submit"
                    isDefault
                    disabled={!text.trim() || add.isPending}
                >
                    Add
                </CommandButton>
            </form>
        </Card>
    );
}
