import { useState } from 'react';
import { type MsgId, useImportStore } from '../state/import-store';

type Props = {
    msgId: MsgId;
    onRegenIdentify: () => void;
    onRegenClassify: () => void;
};

const MAX_PREVIEW = 360;

export function MsgRow({ msgId, onRegenIdentify, onRegenClassify }: Props) {
    const m = useImportStore((s) => s.msgs[msgId]);
    const toggle = useImportStore((s) => s.toggleMsgIncluded);
    const [expanded, setExpanded] = useState(false);

    if (!m) return null;
    const role = m.role;
    const tag = role === 'system' ? 'system' : role;
    const truncated = m.text.length > MAX_PREVIEW;
    const previewText =
        truncated && !expanded ? `${m.text.slice(0, MAX_PREVIEW)}…` : m.text;

    return (
        <div
            className="imp-msg-row"
            data-included={m.included}
            data-role={role}
            data-msg-id={m.id}
        >
            <input
                type="checkbox"
                checked={m.included}
                onChange={() => toggle(msgId)}
                aria-label={`include msg ${m.index}`}
            />
            <div style={{ minWidth: 0 }}>
                <div className="imp-msg-meta">
                    <span>#{m.index < 0 ? 'sys' : m.index}</span>
                    <span className="imp-msg-tag" data-kind={tag}>
                        {tag}
                    </span>
                    {m.identifyStatus === 'running' && (
                        <span style={{ color: 'rgb(255 220 110)' }}>
                            identifying…
                        </span>
                    )}
                    {m.classifyStatus === 'running' && (
                        <span style={{ color: 'rgb(180 220 255)' }}>
                            classifying…
                        </span>
                    )}
                    {m.identifyStatus === 'done' &&
                        m.participants.length > 0 && (
                            <span style={{ color: 'rgba(180,200,230,0.55)' }}>
                                {m.participants.length}p
                            </span>
                        )}
                    {m.classifyStatus === 'done' && m.segments.length > 0 && (
                        <span style={{ color: 'rgba(180,200,230,0.55)' }}>
                            {m.segments.length}s
                        </span>
                    )}
                </div>
                {/* biome-ignore lint/a11y/useKeyWithClickEvents: text expander toggle */}
                {/* biome-ignore lint/a11y/noStaticElementInteractions: text expander toggle */}
                <div
                    className="imp-msg-preview"
                    data-expanded={expanded}
                    onClick={() => truncated && setExpanded(!expanded)}
                >
                    {previewText}
                </div>
            </div>
            <div className="imp-msg-actions">
                <button
                    type="button"
                    className="imp-msg-icon-btn"
                    title="Re-identify participants for this message"
                    onClick={onRegenIdentify}
                    disabled={!m.included}
                >
                    ↻ Pₚ
                </button>
                <button
                    type="button"
                    className="imp-msg-icon-btn"
                    title="Re-classify segments for this message"
                    onClick={onRegenClassify}
                    disabled={!m.included}
                >
                    ↻ Sₛ
                </button>
            </div>
        </div>
    );
}
