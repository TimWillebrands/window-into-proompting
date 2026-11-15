import type { Relation } from '../../api/model';
import { RelationKind } from '../../api/model';

export const KINDS: { kind: RelationKind; label: string }[] = [
    { kind: RelationKind.Together, label: 'Together' },
    { kind: RelationKind.Married, label: 'Married' },
    { kind: RelationKind.Family, label: 'Family' },
    { kind: RelationKind.Friends, label: 'Friends' },
    { kind: RelationKind.Other, label: 'Other' },
];

export const kindLabel = (kind: RelationKind | undefined) =>
    KINDS.find((k) => k.kind === kind)?.label ?? 'Other';

export const isCurrent = (r: Relation) => r.endedAt == null;

export const inOrder = (relations: Relation[]) =>
    [...relations].sort(
        (a, b) =>
            Number(isCurrent(b)) - Number(isCurrent(a)) ||
            Number(b.endedAt ?? 0) - Number(a.endedAt ?? 0),
    );

export function summary(r: Relation): string {
    const said = r.story ?? kindLabel(r.kind);
    return isCurrent(r) ? said : `${said} (past)`;
}
