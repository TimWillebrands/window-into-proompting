import { TextBox } from '@partytown/ui';

interface DaccordSearchBarProps {
    value: string;
    onChange: (value: string) => void;
}

export default function DaccordSearchBar({
    value,
    onChange,
}: DaccordSearchBarProps) {
    return (
        <TextBox
            type="search"
            value={value}
            onChange={(e) => onChange(e.currentTarget.value)}
            placeholder="Search Explore"
            aria-label="Search Explore"
            className="w-full max-w-[320px]"
        />
    );
}
