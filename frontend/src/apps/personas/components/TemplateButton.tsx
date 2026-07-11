import { useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import {
    getGetPersonaQueryKey,
    useGetPersonaDefaultsSuspense,
    usePutPersonaId,
} from '#api/party-zone';
import type { DefaultPersonaTemplate } from '../../../api/model';

function TemplateDropdownContent({
    onSelect,
    onClose,
}: {
    onSelect: (template: DefaultPersonaTemplate) => void;
    onClose: () => void;
}) {
    const defaultsQuery = useGetPersonaDefaultsSuspense();
    const templates: DefaultPersonaTemplate[] = useMemo(() => {
        const data = defaultsQuery.data?.data;
        return Array.isArray(data) ? data : [];
    }, [defaultsQuery.data]);

    return (
        <>
            <button
                type="button"
                aria-label="Close"
                style={{
                    position: 'fixed',
                    inset: 0,
                    zIndex: 99,
                    background: 'transparent',
                    border: 'none',
                    cursor: 'default',
                }}
                onClick={onClose}
            />
            <div
                style={{
                    position: 'absolute',
                    bottom: '100%',
                    left: 0,
                    right: 0,
                    background: '#fff',
                    border: '1px solid #ACA899',
                    boxShadow: '2px 2px 4px rgba(0,0,0,0.2)',
                    zIndex: 100,
                    marginBottom: '2px',
                }}
            >
                {templates.map((t) => (
                    <button
                        key={t.name}
                        type="button"
                        className="w-full text-left"
                        style={{
                            padding: '6px 8px',
                            border: 'none',
                            background: 'transparent',
                            cursor: 'pointer',
                            borderBottom: '1px solid #E8E4DC',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '6px',
                        }}
                        onMouseEnter={(e) => {
                            e.currentTarget.style.background = '#316AC5';
                            e.currentTarget.style.color = '#fff';
                        }}
                        onMouseLeave={(e) => {
                            e.currentTarget.style.background = 'transparent';
                            e.currentTarget.style.color = '#000';
                        }}
                        onClick={() => onSelect(t)}
                    >
                        <img
                            src={`https://robohash.org/${encodeURIComponent(t.name ?? '')}.png?size=20x20`}
                            alt=""
                            style={{
                                width: 20,
                                height: 20,
                                borderRadius: '50%',
                                flexShrink: 0,
                            }}
                        />
                        <span style={{ fontWeight: 600 }}>{t.name}</span>
                    </button>
                ))}
            </div>
        </>
    );
}

export default function TemplateButton({
    onCreated,
}: {
    onCreated: (id: string) => void;
}) {
    const [open, setOpen] = useState(false);
    const queryClient = useQueryClient();

    const upsertMutation = usePutPersonaId({
        mutation: {
            onSuccess: async (response) => {
                await queryClient.invalidateQueries({
                    queryKey: getGetPersonaQueryKey(),
                });
                const created = response.data;
                if (created?.id) {
                    onCreated(created.id);
                }
                setOpen(false);
            },
        },
    });

    const handleSelect = (template: DefaultPersonaTemplate) => {
        const id = crypto.randomUUID();
        upsertMutation.mutate({
            id,
            data: {
                id,
                name: template.name ?? 'Template Persona',
                systemPrompt: template.systemPrompt ?? '',
                bio: template.bio ?? null,
            },
        });
    };

    return (
        <div style={{ position: 'relative' }}>
            <button
                type="button"
                className="w-full"
                title="Start from one of the built-in example personas"
                disabled={upsertMutation.isPending}
                onClick={() => setOpen((v) => !v)}
            >
                {upsertMutation.isPending ? 'Creating...' : 'From Template...'}
            </button>
            {open && (
                <TemplateDropdownContent
                    onSelect={handleSelect}
                    onClose={() => setOpen(false)}
                />
            )}
        </div>
    );
}

export { TemplateDropdownContent };
