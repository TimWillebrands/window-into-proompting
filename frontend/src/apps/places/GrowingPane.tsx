import {
    CheckBox,
    CommandButton,
    Icon,
    ProgressBar,
    Stack,
    TaskPaneSection,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import {
    getGetWorldsPartyIdGrowthQueryKey,
    useGetWorldsPartyIdGrowth,
    usePostWorldsPartyIdGrowthClosePlaceId,
    usePostWorldsPartyIdGrowthOpenLocaleId,
    usePutWorldsPartyIdGrowthAuto,
} from '../../api/party-zone';
import { WORLD_ID } from '../../lib/world';
import { growingSummary } from './places-lib';

export default function GrowingPane() {
    const queryClient = useQueryClient();
    const report = useGetWorldsPartyIdGrowth(WORLD_ID, {
        query: { refetchInterval: 30_000 },
    }).data?.data;
    const refresh = {
        mutation: {
            onSuccess: () =>
                queryClient.invalidateQueries({
                    queryKey: getGetWorldsPartyIdGrowthQueryKey(WORLD_ID),
                }),
        },
    };
    const open = usePostWorldsPartyIdGrowthOpenLocaleId(refresh);
    const close = usePostWorldsPartyIdGrowthClosePlaceId(refresh);
    const auto = usePutWorldsPartyIdGrowthAuto(refresh);
    if (!report) return null;
    return (
        <TaskPaneSection
            title="Growing"
            aside={`${report.rows.length} offscreen`}
        >
            <CheckBox
                checked={report.auto}
                onChange={(e) =>
                    auto.mutate({
                        partyId: WORLD_ID,
                        data: { on: e.currentTarget.checked },
                    })
                }
            >
                Open automatically (one a day)
            </CheckBox>
            {report.rows.slice(0, 8).map((row) => {
                const s = growingSummary(row, Number(report.openAt));
                return (
                    <Stack key={row.localeId} gap="xxs" title={s.who}>
                        <Stack direction="row" align="center" gap="sm">
                            <Stack.Item grow truncate>
                                {s.name}
                            </Stack.Item>
                            <CommandButton
                                size="small"
                                disabled={open.isPending}
                                onClick={() =>
                                    open.mutate({
                                        partyId: WORLD_ID,
                                        localeId: row.localeId,
                                    })
                                }
                            >
                                Open
                            </CommandButton>
                        </Stack>
                        <ProgressBar
                            value={s.share * 100}
                            tone="neutral"
                            label={`${s.name} footfall`}
                        />
                        <span>{s.note}</span>
                    </Stack>
                );
            })}
            {report.opened.map((o) => (
                <Stack key={o.placeId} direction="row" align="center" gap="sm">
                    <Stack
                        as="span"
                        direction="row"
                        align="center"
                        gap="xs"
                        grow
                    >
                        <Icon name="new" />
                        <Stack.Item truncate>
                            {o.name}
                            {o.district ? ` (${o.district})` : ''}
                        </Stack.Item>
                    </Stack>
                    <CommandButton
                        size="small"
                        disabled={close.isPending}
                        onClick={() =>
                            close.mutate({
                                partyId: WORLD_ID,
                                placeId: o.placeId,
                            })
                        }
                    >
                        Close
                    </CommandButton>
                </Stack>
            ))}
        </TaskPaneSection>
    );
}
