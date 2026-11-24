import '@partytown/ui/styles.css';
import '../../../styles.css';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { ROOT_PARTY_ID } from '../../../lib/chat-api';
import RenderPlace from './RenderPlace';

const placeId = location.pathname.split('/').filter(Boolean).at(-1) ?? '';
const search = new URLSearchParams(location.search);
const hour = search.get('hour');
const root = document.getElementById('root');

if (root)
    createRoot(root).render(
        <StrictMode>
            <RenderPlace
                placeId={placeId}
                world={search.get('world') ?? ROOT_PARTY_ID}
                draft={search.get('draft') ?? undefined}
                hour={hour === null ? undefined : Number(hour)}
            />
        </StrictMode>,
    );
