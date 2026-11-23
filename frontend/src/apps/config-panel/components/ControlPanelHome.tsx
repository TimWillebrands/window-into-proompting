import { CategoryItem, CategoryView, Icon } from '@partytown/ui';
import { SECTIONS } from './constants';

export default function ControlPanelHome({
    onNavigate,
}: {
    onNavigate: (section: string) => void;
}) {
    return (
        <CategoryView title="Pick a category" className="min-h-full">
            {SECTIONS.map((s) => (
                <CategoryItem
                    key={s.id}
                    icon={<Icon name={s.icon} size={48} />}
                    title={s.label}
                    description={s.description}
                    onClick={() => onNavigate(s.id)}
                />
            ))}
        </CategoryView>
    );
}
