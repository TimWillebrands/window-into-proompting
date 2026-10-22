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

/** Author view (ADR 0027): Locales gathering footfall, what each still needs, and recent Openings to veto. */
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
        <section className="pl-pane">
            <div className="pl-pane-head">
                Growing
                <small>{report.rows.length} offscreen</small>
            </div>
            <label className="pl-grow">
                <span>
                    <input
                        type="checkbox"
                        checked={report.auto}
                        onChange={(e) =>
                            auto.mutate({
                                partyId: WORLD_ID,
                                data: { on: e.currentTarget.checked },
                            })
                        }
                    />{' '}
                    Open automatically (one a day)
                </span>
            </label>
            {report.rows.slice(0, 8).map((row) => {
                const s = growingSummary(row, Number(report.openAt));
                return (
                    <div key={row.localeId} className="pl-grow" title={s.who}>
                        <span className="pl-grow-name">{s.name}</span>
                        <button
                            type="button"
                            className="pl-grow-act"
                            disabled={open.isPending}
                            onClick={() =>
                                open.mutate({
                                    partyId: WORLD_ID,
                                    localeId: row.localeId,
                                })
                            }
                        >
                            Open
                        </button>
                        <span
                            className="pl-occ-bar"
                            data-level={s.ready ? 'full' : 'some'}
                        >
                            <i style={{ width: `${s.share * 100}%` }} />
                        </span>
                        <span className="pl-grow-note">{s.note}</span>
                    </div>
                );
            })}
            {report.opened.map((o) => (
                <div key={o.placeId} className="pl-grow">
                    <span className="pl-grow-name">
                        🆕 {o.name}
                        {o.district ? ` (${o.district})` : ''}
                    </span>
                    <button
                        type="button"
                        className="pl-grow-act"
                        disabled={close.isPending}
                        onClick={() =>
                            close.mutate({
                                partyId: WORLD_ID,
                                placeId: o.placeId,
                            })
                        }
                    >
                        Close
                    </button>
                </div>
            ))}
        </section>
    );
}
