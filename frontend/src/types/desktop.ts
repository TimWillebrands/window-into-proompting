import * as v from 'valibot';

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
});

export type DesktopLayoutState = v.InferOutput<typeof desktopLayoutSchema>;
export type Window = DesktopLayoutState['windows'][number];
