# Luna with a glass finish

> Status: accepted. The working guide is the `partytown-ui` skill (`.agents/skills/partytown-ui/`).

The look had no written rule. xp.css was dropped for hand-built chrome with a little glass, and since then every app
styled itself: Tailwind utilities, inline styles, per-app CSS, ad-hoc dark mode. Agents copied whatever was nearest,
so the look drifted app by app.

## Decision

1. **80% Windows XP Luna, 20% Liquid Glass.** Luna is what it looks like; glass is how it feels. A screenshot must
   read as XP at a glance. Glass adds a material for floating chrome only (never content) and the comfort of a modern
   toolkit: motion, concentric shapes, focus, roomier targets, readable type. When they conflict, Luna wins on looks
   and comfort wins on use. Every deliberate departure from XP is listed in the skill's deviations register.
2. **The kit is its own package.** `frontend/ui/` (`@partytown/ui`), a pnpm workspace package consumed as source. It
   owns every visual: tokens, parts, icons, fonts, skins. It knows nothing of the domain. Apps compose parts and do
   layout only; they hold no colours, fonts, radii, shadows or durations. Lint enforces the boundary.
3. **Semantic tokens, schemes over them.** Parts read `--ui-*` tokens only. A scheme sets their values: Luna Blue
   (the reference) and Noir (dark, after Royale Noir). Dark mode lives in the kit, not in apps.
4. **Parts carry XP names.** Window, Dialog, CommandButton, TaskPane, Balloon, ListView. Glass is a property of the
   floating parts, not a part of its own.
5. **Skins are the sanctioned exception.** A Winamp-style app may replace Luna chrome with a named Skin in
   `ui/skins/`, the only place raw values are allowed. It still keeps focus, reduced-motion and window behaviour.

## Consequences

- The skill states the rules, the kit Storybook shows them, this ADR says why. Values live only in `tokens.css`.
- Agents verify UI work with lint, the skill's checklist, part stories and a visual comparison against references.
- Apps migrate part by part; per-app CSS shrinks to what is truly domain (3D scenes, figures).
