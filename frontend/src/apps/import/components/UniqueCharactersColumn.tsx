import { useImportStore } from '../state/import-store';
import { CharCard } from './CharCard';

export function UniqueCharactersColumn() {
    const order = useImportStore((s) => s.roster.charOrder);
    const phaseIdentify = useImportStore((s) => s.phase.identify);

    return (
        <section className="imp-col">
            <header className="imp-col-head">
                <span>Unique characters</span>
                <span className="imp-col-count">{order.length} total</span>
            </header>
            <div className="imp-col-body">
                {order.length === 0 ? (
                    <div
                        style={{
                            padding: 14,
                            fontSize: 11,
                            color: 'rgba(220,230,250,0.55)',
                            textAlign: 'center',
                            lineHeight: 1.5,
                        }}
                    >
                        {phaseIdentify === 'running'
                            ? 'Waiting for roster sync…'
                            : 'Run "Identify Characters" to discover the cast.'}
                    </div>
                ) : (
                    order.map((id) => <CharCard key={id} charId={id} />)
                )}
            </div>
        </section>
    );
}
