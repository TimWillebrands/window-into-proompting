import { useCallback, useEffect, useRef } from 'react';
import { type CharId, useImportStore } from '../state/import-store';

/**
 * Hover/pin hook bound to one character. Hover sets `ui.hoveredCharId`
 * (debounced clear so chip-edge transitions don't flicker); click toggles the
 * pin. Pinning also scrolls col3 to the first segment for that char.
 */
const HOVER_DEBOUNCE = 60;

export function useCharHover(charId: CharId | null) {
    const setHovered = useImportStore((s) => s.setHoveredChar);
    const setPinned = useImportStore((s) => s.setPinnedChar);
    const pinnedCharId = useImportStore((s) => s.ui.pinnedCharId);
    const clearTimer = useRef<number | null>(null);

    const onMouseEnter = useCallback(() => {
        if (!charId) return;
        if (clearTimer.current !== null) {
            window.clearTimeout(clearTimer.current);
            clearTimer.current = null;
        }
        setHovered(charId);
    }, [charId, setHovered]);

    const onMouseLeave = useCallback(() => {
        if (clearTimer.current !== null)
            window.clearTimeout(clearTimer.current);
        clearTimer.current = window.setTimeout(() => {
            setHovered(null);
            clearTimer.current = null;
        }, HOVER_DEBOUNCE);
    }, [setHovered]);

    const onClickPin = useCallback(() => {
        if (!charId) return;
        const next = pinnedCharId === charId ? null : charId;
        setPinned(next);
        if (next) {
            queueMicrotask(() => {
                const target = document.querySelector(
                    `[data-segment-first-of-char="${charId}"]`,
                );
                target?.scrollIntoView({
                    block: 'nearest',
                    behavior: 'smooth',
                });
            });
        }
    }, [charId, pinnedCharId, setPinned]);

    useEffect(
        () => () => {
            if (clearTimer.current !== null)
                window.clearTimeout(clearTimer.current);
        },
        [],
    );

    return { onMouseEnter, onMouseLeave, onClickPin };
}
