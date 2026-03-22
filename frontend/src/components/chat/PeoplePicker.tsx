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
        return (
            <p className="text-[11px]" style={{ color: '#808080' }}>
                No personas available yet.
            </p>
        );
    }

    return (
        <div className="flex flex-wrap gap-1">
            {people.map((person) => {
                const selected = selectedSet.has(person.id);
                return (
                    <label
                        key={person.id}
                        className={`flex items-center gap-1 ${compact ? 'text-[10px]' : 'text-xs'} cursor-pointer select-none`}
                        style={{
                            padding: compact ? '1px 4px' : '2px 6px',
                            color: person.disabled
                                ? '#808080'
                                : selected
                                  ? '#fff'
                                  : '#000',
                            background: selected ? '#316AC5' : 'transparent',
                            border: '1px solid',
                            borderColor: selected ? '#316AC5' : '#ACA899',
                            borderRadius: '2px',
                            opacity: person.disabled ? 0.6 : 1,
                        }}
                    >
                        <input
                            type="checkbox"
                            checked={selected}
                            disabled={person.disabled}
                            onChange={() => {
                                const next = selected
                                    ? selectedIds.filter(
                                          (id) => id !== person.id,
                                      )
                                    : selectedIds.concat(person.id);
                                onChange(next);
                            }}
                            style={{ display: 'none' }}
                        />
                        <span style={{ lineHeight: 1 }}>{person.name}</span>
                    </label>
                );
            })}
        </div>
    );
}
