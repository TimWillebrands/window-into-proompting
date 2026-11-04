import { Suspense, useState } from 'react';
import { useGetWorldsPartyIdJobsSuspense } from '#api/party-zone';
import { WORLD_ID } from '../../lib/world';
import { CompaniesPanel } from './CompaniesPanel';
import { PracticesPanel } from './PracticesPanel';

/** Real work (ADR 0030): Companies on real Markets and Practices with private Sessions. */
export default function RealWorkApp() {
    const people = useGetWorldsPartyIdJobsSuspense(WORLD_ID).data.data ?? [];
    const [tab, setTab] = useState<'companies' | 'practices'>('companies');

    return (
        <div className="app-surface flex h-full flex-col">
            <div
                role="tablist"
                className="flex"
                style={{ gap: 2, padding: '6px 6px 0' }}
            >
                <button
                    type="button"
                    role="tab"
                    aria-selected={tab === 'companies'}
                    onClick={() => setTab('companies')}
                >
                    🏦 Companies
                </button>
                <button
                    type="button"
                    role="tab"
                    aria-selected={tab === 'practices'}
                    onClick={() => setTab('practices')}
                >
                    🛋️ Practices
                </button>
            </div>
            <div
                className="min-h-0 flex-1 overflow-y-auto"
                style={{ padding: 6 }}
            >
                <Suspense fallback={null}>
                    {tab === 'companies' ? (
                        <CompaniesPanel people={people} />
                    ) : (
                        <PracticesPanel people={people} />
                    )}
                </Suspense>
            </div>
        </div>
    );
}
