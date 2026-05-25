import { useMemo } from 'react';
import { useImportStore } from '../state/import-store';
import { SegmentRow } from './SegmentRow';

export function MessagePartsColumn() {
    const order = useImportStore((s) => s.msgOrder);
    const msgs = useImportStore((s) => s.msgs);

    return (
        <section className="imp-col">
            <header className="imp-col-head">
                <span>Message parts</span>
                <span className="imp-col-count">segments</span>
            </header>
            <div className="imp-col-body" data-col="segments">
                {order.map((id) => {
                    const m = msgs[id];
                    if (!m || !m.included) return null;
                    if (
                        m.segments.length === 0 &&
                        m.classifyStatus !== 'running'
                    ) {
                        return null;
                    }
                    return <SegmentGroup key={id} msgId={id} />;
                })}
            </div>
        </section>
    );
}

function SegmentGroup({ msgId }: { msgId: string }) {
    const segments = useImportStore((s) => s.msgs[msgId]?.segments ?? []);
    const status = useImportStore((s) => s.msgs[msgId]?.classifyStatus);
    const index = useImportStore((s) => s.msgs[msgId]?.index);
    const role = useImportStore((s) => s.msgs[msgId]?.role);

    const firstByChar = useMemo(() => {
        const seen = new Set<string>();
        const map = new Map<string, boolean>();
        for (const seg of segments) {
            if (!seg.charId) continue;
            if (!seen.has(seg.charId)) {
                seen.add(seg.charId);
                map.set(seg.id, true);
            }
        }
        return map;
    }, [segments]);

    return (
        <div className="imp-seg-group" data-msg-id={msgId}>
            <div className="imp-seg-group-head">
                <span>
                    chunk #{index} · {role}
                </span>
                {status === 'running' && (
                    <span style={{ color: 'rgb(180 220 255)' }}>
                        classifying…
                    </span>
                )}
                <span style={{ marginLeft: 'auto' }}>
                    {segments.length} seg
                </span>
            </div>
            {segments.map((seg) => (
                <SegmentRow
                    key={seg.id}
                    msgId={msgId}
                    segment={seg}
                    firstOfChar={firstByChar.get(seg.id) === true}
                />
            ))}
        </div>
    );
}
