import { createFileRoute } from '@tanstack/react-router';
import RenderPlace from '../apps/places/render/RenderPlace';
import { ROOT_PARTY_ID } from '../lib/chat-api';

interface RenderSearch {
    world: string;
    draft?: string;
    hour?: number;
}

export const Route = createFileRoute('/render/place/$placeId')({
    ssr: false,
    validateSearch: (s: Record<string, unknown>): RenderSearch => ({
        world: typeof s.world === 'string' ? s.world : ROOT_PARTY_ID,
        draft: typeof s.draft === 'string' ? s.draft : undefined,
        hour: typeof s.hour === 'number' ? s.hour : undefined,
    }),
    component: RenderRoute,
});

function RenderRoute() {
    const { placeId } = Route.useParams();
    const search = Route.useSearch();
    return <RenderPlace placeId={placeId} {...search} />;
}
