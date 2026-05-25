import { useImportStore } from '../state/import-store';
import { MsgRow } from './MsgRow';

type Props = {
    onRegenIdentify: (msgId: string) => void;
    onRegenClassify: (msgId: string) => void;
};

export function MessageColumn({ onRegenIdentify, onRegenClassify }: Props) {
    const order = useImportStore((s) => s.msgOrder);
    return (
        <section className="imp-col">
            <header className="imp-col-head">
                <span>Messages</span>
                <span className="imp-col-count">{order.length} rows</span>
            </header>
            <div className="imp-col-body" data-col="messages">
                {order.map((id) => (
                    <MsgRow
                        key={id}
                        msgId={id}
                        onRegenIdentify={() => onRegenIdentify(id)}
                        onRegenClassify={() => onRegenClassify(id)}
                    />
                ))}
            </div>
        </section>
    );
}
