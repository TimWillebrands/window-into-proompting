import {
    Console,
    ConsoleLine,
    type ConsoleTone,
    Hyperlink,
    Icon,
    Post,
    TaskPane,
    TaskPaneSection,
} from '@partytown/ui';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import {
    getGetWorldsPartyIdCduQueryKey,
    useGetWorldsPartyIdCdu,
    useGetWorldsPartyIdCduLines,
} from '#api/party-zone';
import type { CduLineSummary, CduOverview } from '../../api/model';
import { useWorldEvents, WORLD_ID } from '../../lib/world';
import type { WorldEventData } from '../../lib/world-bus';
import {
    allowanceLine,
    exchangeWord,
    linePrefix,
    mergeLines,
    mindLine,
    prLine,
    SAID,
    shortSha,
} from './cdu-lib';

const TONES: Record<string, ConsoleTone> = {
    heard: 'neutral',
    thought: 'muted',
    tool: 'go',
    failed: 'danger',
    status: 'muted',
};

interface CduFrame {
    lines?: CduLineSummary[];
}

function Terminal() {
    const [lines, setLines] = useState<CduLineSummary[]>([]);
    const end = useRef<HTMLDivElement>(null);
    const history = useGetWorldsPartyIdCduLines(WORLD_ID);

    useEffect(() => {
        if (history.data?.status === 200) {
            const fetched = history.data.data;
            setLines((held) => mergeLines(held, fetched));
        }
    }, [history.data]);

    useWorldEvents((envelope) => {
        const { kind, payload } = envelope.data as WorldEventData;
        if (kind !== 'cdu') return;
        const frame = payload as CduFrame;
        if (frame.lines) {
            const incoming = frame.lines;
            setLines((held) => mergeLines(held, incoming));
        }
    });

    const count = lines.length;
    useEffect(() => {
        if (count > 0) end.current?.scrollIntoView({ block: 'end' });
    }, [count]);

    return (
        <Console role="log" aria-live="polite" className="min-h-[120px] flex-1">
            <ConsoleLine tone="muted">C:\CDU&gt; listen</ConsoleLine>
            {lines.map((l) => (
                <ConsoleLine
                    key={l.n}
                    prompt={linePrefix(l.kind)}
                    tone={TONES[l.kind] ?? 'default'}
                >
                    {l.text}
                </ConsoleLine>
            ))}
            <div ref={end} />
        </Console>
    );
}

function State({ cdu }: { cdu: CduOverview }) {
    return (
        <>
            <TaskPaneSection
                title="CDU"
                icon={<Icon name="cdu" size={32} />}
                special
            >
                <p>
                    {!cdu.up
                        ? 'The CDU is dark'
                        : cdu.awake
                          ? `Awake, hearing ${cdu.hearing ?? 'someone'}`
                          : 'Asleep until spoken to'}
                </p>
                {cdu.model ? <p>{cdu.model}</p> : null}
                <p>{allowanceLine(cdu.allowance)}</p>
            </TaskPaneSection>
            <TaskPaneSection title="Trunk">
                {cdu.trunk.length === 0 ? (
                    <p>Nothing the world lacks.</p>
                ) : (
                    <ul className="grid gap-1.5">
                        {cdu.trunk.map((c) => (
                            <li key={c.sha} className="wrap-anywhere">
                                {shortSha(c.sha)} {c.subject}{' '}
                                <span className="block">for {c.author}</span>
                            </li>
                        ))}
                    </ul>
                )}
            </TaskPaneSection>
            {cdu.models.length > 0 ? (
                <TaskPaneSection title="Minds, cheapest first">
                    <ul className="grid gap-1.5">
                        {cdu.models.map((m) => (
                            <li key={m.id} className="wrap-anywhere">
                                {mindLine(m)}
                                <span className="block">{m.label}</span>
                            </li>
                        ))}
                    </ul>
                </TaskPaneSection>
            ) : null}
            <TaskPaneSection title="Agora">
                {cdu.pr ? (
                    <p>
                        <Hyperlink
                            href={cdu.pr.url}
                            target="_blank"
                            rel="noreferrer"
                        >
                            {cdu.pr.title}
                        </Hyperlink>
                        <span className="block">{prLine(cdu.pr)}</span>
                    </p>
                ) : (
                    <p>No PR open.</p>
                )}
            </TaskPaneSection>
        </>
    );
}

function Said({ cdu }: { cdu: CduOverview }) {
    if (cdu.exchanges.length === 0) return <p>Nobody has spoken to it yet.</p>;
    return (
        <div className="grid">
            {cdu.exchanges.map((e) => (
                <Post
                    key={e.turnId}
                    variant="flat"
                    size="compact"
                    title={`${e.speakerName} ${SAID[e.kind] ?? 'said'}`}
                    meta={`${exchangeWord(e)}${e.model ? ` · woke ${e.model}` : ''}`}
                    replies={
                        e.reply ? (
                            <Post variant="flat" size="compact">
                                {e.reply}
                            </Post>
                        ) : undefined
                    }
                >
                    {e.text}
                </Post>
            ))}
        </div>
    );
}

export default function CduApp() {
    const queryClient = useQueryClient();
    const overview = useGetWorldsPartyIdCdu(WORLD_ID);

    useWorldEvents((envelope) => {
        const { kind, payload } = envelope.data as WorldEventData;
        if (kind === 'cdu' && !(payload as CduFrame).lines)
            queryClient.invalidateQueries({
                queryKey: getGetWorldsPartyIdCduQueryKey(WORLD_ID),
            });
    });

    const cdu = overview.data?.status === 200 ? overview.data.data : null;
    if (!cdu)
        return (
            <div className="flex h-full items-center justify-center">
                Connecting…
            </div>
        );
    if (!cdu.enabled)
        return (
            <div className="flex h-full items-center justify-center">
                No CDU is configured for this World.
            </div>
        );

    return (
        <div className="flex h-full min-h-0">
            <TaskPane className="w-[280px] flex-none">
                <State cdu={cdu} />
                <TaskPaneSection title="Spoken to it">
                    <Said cdu={cdu} />
                </TaskPaneSection>
            </TaskPane>
            <div className="flex min-h-0 min-w-0 flex-1 flex-col p-2">
                <Terminal />
            </div>
        </div>
    );
}
