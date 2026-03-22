import { createFileRoute } from '@tanstack/react-router';
import { desktopLayoutSchema } from '../types/desktop';
import DesktopExperience from '../views/DesktopExperience';
import { useDesktopStore } from '../lib/desktop-context';
import { useEffect } from 'react';

export const Route = createFileRoute('/')({
    validateSearch: desktopLayoutSchema,
    beforeLoad: ({ search }) => {
        useDesktopStore.setState({ desktopState: search });
    },
    component: Page,
});

function Page() {
    const desktop = Route.useSearch();

    useEffect(() => {
        useDesktopStore.setState({ desktopState: desktop });
    }, [desktop]);

    return <DesktopExperience />;
}
