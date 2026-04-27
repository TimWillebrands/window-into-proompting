import * as v from 'valibot';

const windowSnapshot = v.object({
    x: v.number(),
    y: v.number(),
    width: v.number(),
    height: v.number(),
});

export const desktopLayoutSchema = v.object({
    windows: v.optional(
        v.fallback(
            v.record(
                v.string(),
                v.object({
                    id: v.string(),
                    appId: v.optional(v.fallback(v.string(), ''), ''),
                    title: v.string(),
                    icon: v.string(),
                    x: v.number(),
                    y: v.number(),
                    width: v.number(),
                    height: v.number(),
                    zIndex: v.number(),
                    minimized: v.optional(
                        v.fallback(v.boolean(), false),
                        false,
                    ),
                    maximized: v.optional(
                        v.fallback(v.boolean(), false),
                        false,
                    ),
                    restoreBounds: v.optional(
                        v.fallback(v.nullable(windowSnapshot), null),
                        null,
                    ),
                    props: v.optional(
                        v.fallback(v.record(v.string(), v.unknown()), {}),
                        {},
                    ),
                }),
            ),
            {},
        ),
        {},
    ),
    order: v.optional(v.fallback(v.array(v.string()), []), []),
    focusedId: v.fallback(v.nullable(v.string()), null),
    zCounter: v.optional(v.fallback(v.number(), 0), 0),
    startMenuOpen: v.optional(v.fallback(v.boolean(), false), false),
});

export type DesktopLayoutState = v.InferOutput<typeof desktopLayoutSchema>;
export type Window = DesktopLayoutState['windows'][number];
