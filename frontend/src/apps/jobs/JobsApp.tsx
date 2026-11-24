import {
    Icon,
    TaskPane,
    TaskPaneLink,
    TaskPaneSection,
    WindowLayout,
} from '@partytown/ui';
import { Suspense, useCallback } from 'react';
import { useWindowInstance } from '../../components/desktop/window-instance-context';
import { useDesktopContext } from '../../lib/desktop-context';
import { CompaniesPage, CompanyPage } from './CompanyPages';
import { DoorsPage } from './DoorsPage';
import { bookReturn, placeFrom, shiftLabel, type WorkPlace } from './jobs-lib';
import { PeoplePage, PersonPage } from './PeoplePages';
import { PracticesPage } from './PracticesPage';
import { Signed } from './parts';
import { useWork, WorkProvider } from './work-context';

const PAGE_LABELS: Record<WorkPlace['page'], string> = {
    people: 'Everyone',
    person: 'Person',
    companies: 'Companies',
    company: 'Company',
    practices: 'Practices',
    doors: 'Doors',
};

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
        <WorkProvider go={go}>
            <WindowLayout
                scroll="none"
                contentLabel={PAGE_LABELS[here.page]}
                taskPaneLabel="Work"
                taskPane={<WorkPane here={here} />}
            >
                <Suspense fallback={null}>
                    <Page here={here} />
                </Suspense>
            </WindowLayout>
        </WorkProvider>
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

function WorkPane({ here }: { here: WorkPlace }) {
    const { people, companies, practices, go } = useWork();
    const at = (page: WorkPlace['page'], id?: string) =>
        here.page === page && (!id || ('id' in here && here.id === id))
            ? ('page' as const)
            : undefined;

    return (
        <TaskPane>
            <TaskPaneSection
                title="People"
                aside={people.length || undefined}
                icon={<Icon name="users" size={32} />}
            >
                <TaskPaneLink
                    icon={<Icon name="users" />}
                    aria-current={at('people')}
                    onClick={() => go({ page: 'people' })}
                >
                    Everyone
                </TaskPaneLink>
                {people.map((p) => (
                    <TaskPaneLink
                        key={p.participantId}
                        icon={<Icon name="user" />}
                        aria-current={at('person', p.participantId)}
                        onClick={() =>
                            go({ page: 'person', id: p.participantId })
                        }
                    >
                        {p.name}
                        {shiftLabel(p) ? ` · ${shiftLabel(p)}` : ''}
                    </TaskPaneLink>
                ))}
            </TaskPaneSection>
            <TaskPaneSection
                title="Companies"
                aside={companies.length || undefined}
                icon={<Icon name="company" size={32} />}
            >
                <TaskPaneLink
                    icon={<Icon name="folder" />}
                    aria-current={at('companies')}
                    onClick={() => go({ page: 'companies' })}
                >
                    All companies
                </TaskPaneLink>
                {companies.map((c) => (
                    <TaskPaneLink
                        key={c.id}
                        icon={<Icon name="company" />}
                        aria-current={at('company', c.id)}
                        onClick={() => go({ page: 'company', id: c.id })}
                    >
                        {c.name} <Signed fraction={bookReturn(c)} />
                    </TaskPaneLink>
                ))}
                {companies.length === 0 ? <p>None yet</p> : null}
            </TaskPaneSection>
            <TaskPaneSection
                title="Practices"
                aside={practices.length || undefined}
                icon={<Icon name="sofa" size={32} />}
            >
                <TaskPaneLink
                    icon={<Icon name="folder" />}
                    aria-current={at('practices')}
                    onClick={() => go({ page: 'practices' })}
                >
                    All practices
                </TaskPaneLink>
                {practices.map((p) => (
                    <TaskPaneLink
                        key={p.participantId}
                        icon={<Icon name="user" />}
                        aria-current={at('person', p.participantId)}
                        onClick={() =>
                            go({ page: 'person', id: p.participantId })
                        }
                    >
                        {p.name} · {p.title}
                    </TaskPaneLink>
                ))}
                {practices.length === 0 ? <p>None yet</p> : null}
            </TaskPaneSection>
            <TaskPaneSection
                title="Doors"
                icon={<Icon name="door" size={32} />}
            >
                <TaskPaneLink
                    icon={<Icon name="key" />}
                    aria-current={at('doors')}
                    onClick={() => go({ page: 'doors' })}
                >
                    Who may come in
                </TaskPaneLink>
            </TaskPaneSection>
        </TaskPane>
    );
}
