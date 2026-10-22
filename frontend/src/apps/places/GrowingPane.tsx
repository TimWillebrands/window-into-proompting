import { useGetWorldsPartyIdGrowth } from '../../api/party-zone';
import { WORLD_ID } from '../../lib/world';
import { growingSummary } from './places-lib';

/** Author view (ADR 0027): which Locales are gathering footfall, and what each still needs to open. */
export default function GrowingPane() {
    const rows =
        useGetWorldsPartyIdGrowth(WORLD_ID, {
            query: { refetchInterval: 30_000 },
        }).data?.data ?? [];
    if (rows.length === 0) return null;
    return (
        <section className="pl-pane">
            <div className="pl-pane-head">
                Growing
                <small>{rows.length} offscreen</small>
            </div>
            {rows.slice(0, 8).map((row) => {
                const s = growingSummary(row);
                return (
                    <div key={row.localeId} className="pl-grow" title={s.who}>
                        <span className="pl-grow-name">{s.name}</span>
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
        </section>
    );
}
