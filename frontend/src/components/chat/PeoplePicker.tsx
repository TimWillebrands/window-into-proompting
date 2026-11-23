import { CheckBox, Text } from '@partytown/ui';

export interface PeoplePickerPerson {
    id: string;
    name: string;
    subtitle?: string;
    disabled?: boolean;
}

interface PeoplePickerProps {
    people: PeoplePickerPerson[];
    selectedIds: string[];
    onChange: (nextSelectedIds: string[]) => void;
    compact?: boolean;
}

export default function PeoplePicker({
    people,
    selectedIds,
    onChange,
    compact = false,
}: PeoplePickerProps) {
    const selectedSet = new Set(selectedIds);

    if (people.length === 0) {
        return <Text tone="muted">No personas available yet.</Text>;
    }

    return (
        <div
            className={`flex flex-wrap ${compact ? 'gap-x-2 gap-y-1' : 'gap-x-4 gap-y-2'}`}
        >
            {people.map((person) => {
                const selected = selectedSet.has(person.id);
                return (
                    <CheckBox
                        key={person.id}
                        checked={selected}
                        disabled={person.disabled}
                        onChange={() => {
                            const next = selected
                                ? selectedIds.filter((id) => id !== person.id)
                                : selectedIds.concat(person.id);
                            onChange(next);
                        }}
                    >
                        {person.name}
                    </CheckBox>
                );
            })}
        </div>
    );
}
