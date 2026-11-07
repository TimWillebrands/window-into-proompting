import { Suspense, useCallback } from 'react';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import { useDesktopContext } from '../../lib/desktop-context';
import { CompaniesPage, CompanyPage } from './CompanyPages';
import { DoorsPage } from './DoorsPage';
import './jobs.css';
import {
    bookReturn,
    placeFrom,
    shiftLabel,
    signedPercent,
    toneOf,
    type WorkPlace,
} from './jobs-lib';
import { PeoplePage, PersonPage } from './PeoplePages';
import { PracticesPage } from './PracticesPage';
import { useWork, WorkProvider } from './work-context';

/** Work & Wants (ADR 0026, 0030): who does what, the Companies they trade for, the Practices they run. */
export default function JobsApp({ page, id }: { page?: string; id?: string }) {
    const { setWindowProps } = useDesktopContext();
    const { windowId } = useWindowInstance();
    const go = useCallback(
        (place: WorkPlace) =>
            setWindowProps(windowId, (p) => ({
                ...p,
                page: place.page,
                id: 'id' in place ? place.id : undefined,
            })),
        [setWindowProps, windowId],
    );
    const here = placeFrom(page, id);

    return (
        <div className="app-surface wk-app">
            <WorkProvider go={go}>
                <TaskPane here={here} />
                <main className="wk-main">
                    <Suspense fallback={null}>
                        <Page here={here} />
                    </Suspense>
                </main>
            </WorkProvider>
        </div>
    );
}

function Page({ here }: { here: WorkPlace }) {
    const { people, companies } = useWork();
    if (here.page === 'person') {
        const person = people.find((p) => p.participantId === here.id);
        if (person) return <PersonPage key={here.id} person={person} />;
    }
    if (here.page === 'company') {
        const company = companies.find((c) => c.id === here.id);
        if (company) return <CompanyPage key={here.id} company={company} />;
    }
    if (here.page === 'companies') return <CompaniesPage />;
    if (here.page === 'practices') return <PracticesPage />;
    if (here.page === 'doors') return <DoorsPage />;
    return <PeoplePage />;
}

/** The XP Explorer task pane: a group per page, and the workplaces' doors. */
function TaskPane({ here }: { here: WorkPlace }) {
    const { people, companies, practices, go } = useWork();
    const at = (page: WorkPlace['page'], id?: string) =>
        here.page === page && (!id || ('id' in here && here.id === id))
            ? ('page' as const)
            : undefined;

    return (
        <nav className="wk-pane" aria-label="Work">
            <section className="wk-group">
                <button
                    type="button"
                    className="xp-bare wk-group-head"
                    aria-current={at('people')}
                    onClick={() => go({ page: 'people' })}
                >
                    👥 People
                </button>
                <ul>
                    {people.map((p) => (
                        <li key={p.participantId}>
                            <button
                                type="button"
                                className="xp-bare wk-link"
                                aria-current={at('person', p.participantId)}
                                onClick={() =>
                                    go({ page: 'person', id: p.participantId })
                                }
                            >
                                <span>{p.name}</span>
                                {shiftLabel(p) ? (
                                    <small>{shiftLabel(p)}</small>
                                ) : null}
                            </button>
                        </li>
                    ))}
                </ul>
            </section>
            <section className="wk-group">
                <button
                    type="button"
                    className="xp-bare wk-group-head"
                    aria-current={at('companies')}
                    onClick={() => go({ page: 'companies' })}
                >
                    🏦 Companies
                </button>
                <ul>
                    {companies.map((c) => {
                        const change = bookReturn(c);
                        return (
                            <li key={c.id}>
                                <button
                                    type="button"
                                    className="xp-bare wk-link"
                                    aria-current={at('company', c.id)}
                                    onClick={() =>
                                        go({ page: 'company', id: c.id })
                                    }
                                >
                                    <span>{c.name}</span>
                                    <small data-tone={toneOf(change)}>
                                        {signedPercent(change)}
                                    </small>
                                </button>
                            </li>
                        );
                    })}
                    {companies.length === 0 ? (
                        <li className="wk-pane-hint">None yet</li>
                    ) : null}
                </ul>
            </section>
            <section className="wk-group">
                <button
                    type="button"
                    className="xp-bare wk-group-head"
                    aria-current={at('practices')}
                    onClick={() => go({ page: 'practices' })}
                >
                    🛋️ Practices
                </button>
                <ul>
                    {practices.map((p) => (
                        <li key={p.participantId}>
                            <button
                                type="button"
                                className="xp-bare wk-link"
                                aria-current={at('person', p.participantId)}
                                onClick={() =>
                                    go({ page: 'person', id: p.participantId })
                                }
                            >
                                <span>{p.name}</span>
                                <small>{p.title}</small>
                            </button>
                        </li>
                    ))}
                    {practices.length === 0 ? (
                        <li className="wk-pane-hint">None yet</li>
                    ) : null}
                </ul>
            </section>
            <section className="wk-group">
                <button
                    type="button"
                    className="xp-bare wk-group-head"
                    aria-current={at('doors')}
                    onClick={() => go({ page: 'doors' })}
                >
                    🚪 Doors
                </button>
            </section>
        </nav>
    );
}
