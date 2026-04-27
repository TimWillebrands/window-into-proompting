import CategoryTile from './CategoryTile';
import { SECTIONS } from './constants';

export default function ControlPanelHome({
    onNavigate,
}: {
    onNavigate: (section: string) => void;
}) {
    return (
        <div>
            <h2
                style={{
                    color: '#fff',
                    fontSize: 22,
                    fontWeight: 300,
                    margin: '0 0 20px 0',
                    borderBottom: '1px solid rgba(255,255,255,0.4)',
                    paddingBottom: 8,
                    textShadow: '0 1px 2px rgba(0,0,0,0.3)',
                }}
            >
                Pick a category
            </h2>
            <div
                style={{
                    display: 'grid',
                    gridTemplateColumns:
                        'repeat(auto-fill, minmax(200px, 1fr))',
                    gap: 12,
                }}
            >
                {SECTIONS.map((s) => (
                    <CategoryTile
                        key={s.id}
                        icon={s.icon}
                        label={s.label}
                        description={s.description}
                        onClick={() => onNavigate(s.id)}
                    />
                ))}
            </div>
        </div>
    );
}
