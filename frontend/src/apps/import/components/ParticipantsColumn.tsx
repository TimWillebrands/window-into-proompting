import { useMemo } from 'react';
import { useImportStore } from '../state/import-store';
import { ChipPill } from './ChipPill';

export function ParticipantsColumn() {
    const order = useImportStore((s) => s.msgOrder);
    return (
        <section className="imp-col">
            <header className="imp-col-head">
                <span>Participants</span>
                <span className="imp-col-count">per message</span>
            </header>
            <div className="imp-col-body">
                {order.map((id) => (
                    <ParticipantBlock key={id} msgId={id} />
                ))}
            </div>
        </section>
    );
}

function ParticipantBlock({ msgId }: { msgId: string }) {
    const participants = useImportStore((s) => s.msgs[msgId]?.participants);
    const included = useImportStore((s) => s.msgs[msgId]?.included);
    const mentionIds = useMemo(
        () => participants?.map((p) => p.mentionId) ?? [],
        [participants],
    );

    return (
        <div
            className="imp-participant-block"
            data-msg-id={msgId}
            style={{ opacity: included ? 1 : 0.4 }}
        >
            {mentionIds.map((mid) => (
                <ChipPill key={mid} msgId={msgId} mentionId={mid} />
            ))}
        </div>
    );
}
