import {
    CheckBox,
    CommandButton,
    Icon,
    ProgressBar,
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
                    <div
                        key={row.localeId}
                        className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-1.5 gap-y-0.5"
                        title={s.who}
                    >
                        <span className="truncate">{s.name}</span>
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
                        <ProgressBar
                            className="col-span-full"
                            value={s.share * 100}
                            tone="neutral"
                            label={`${s.name} footfall`}
                        />
                        <span className="col-span-full">{s.note}</span>
                    </div>
                );
            })}
            {report.opened.map((o) => (
                <div
                    key={o.placeId}
                    className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-1.5"
                >
                    <span className="flex min-w-0 items-center gap-1">
                        <Icon name="new" />
                        <span className="truncate">
                            {o.name}
                            {o.district ? ` (${o.district})` : ''}
                        </span>
                    </span>
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
                </div>
            ))}
        </TaskPaneSection>
    );
}
