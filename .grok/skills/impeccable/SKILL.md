---
name: impeccable
description: >
  Craft-quality UI design skill (Impeccable): critique, polish, and implement
  interfaces with intentional typography, color, spacing, hierarchy, and motion.
  Avoids generic AI aesthetics. Use when the user asks for impeccable design,
  UI polish, visual QA, design critique, make it look premium/refined, anti-slop
  styling, or runs /impeccable. Works for Avalonia/XAML, web, and desktop UI —
  not a rename of frontend-design (build-from-scratch bold creativity).
metadata:
  short-description: "Craft-quality UI critique, polish, and design"
---

# /impeccable — Design craft

Apply **Impeccable** design craft: intentional, refined interfaces that feel
designed by a human with taste — not default AI chrome.

## When this skill applies

- User runs `/impeccable` or asks to polish, refine, or critique UI
- Visual QA of existing screens, XAML, CSS, or mockups
- Making something look premium, calm, or on-brand
- Fixing "AI slop" (generic purple gradients, Inter-everywhere, uneven spacing)

**Not the primary skill for:** inventing a wildly new aesthetic from a blank
canvas (prefer `frontend-design` when available). Impeccable can still guide
implementation, but its strength is **craft, consistency, and polish**.

## Modes

Infer mode from the request; default to **Polish** if ambiguous.

| Mode | Intent | Output |
|------|--------|--------|
| **Critique** | Review only | Findings by severity; no large rewrites unless asked |
| **Polish** | Improve existing UI | Focused visual fixes in real files |
| **Implement** | Build/adjust UI with craft | Working code that meets the checklist below |

Optional focus after the command: `/impeccable spacing` · `/impeccable header` ·
`/impeccable Luma compose bar`.

## Workflow

1. **Context first**
   - Read the target UI files (XAML, code-behind styles, CSS, theme tokens).
   - In this repo: `AGENTS.md` UI map, `LumaTheme.cs`, `App.axaml`, `MainWindow.axaml`.
   - Honor existing theme tokens and brand — do not invent a parallel palette.
2. **Commit to intent** (one sentence)
   - Purpose, audience, and tone (e.g. calm dock utility, luminous aurora, minimal orbit).
3. **Audit against the craft checklist** (below).
4. **Change the fewest surfaces that raise quality the most**
   - Prefer token/theme and shared styles over one-off magic numbers.
   - Keep edits small and reversible; match project conventions.
5. **Verify**
   - Re-read changed files; check contrast, alignment, states (hover/disabled/busy).
   - Summarize what improved and what was left alone on purpose.

## Craft checklist

### Hierarchy & layout
- One clear primary action per region; secondary actions quieter
- Consistent alignment grids; optical balance over pure math when needed
- Spacing scale (e.g. 4/8/12/16/24/32) — no random 7px/13px gaps
- Density matches context: docks stay compact; marketing can breathe

### Typography
- Distinct roles: display / title / body / caption / mono
- Line length and line-height readable; avoid walls of same-weight text
- Prefer project fonts (Luma: Satoshi when available) over default system stacks
  for brand surfaces; never mix three+ unrelated families

### Color & surfaces
- Use design tokens (`LumaTheme`, resource keys) — no hard-coded one-offs unless
  introducing a documented token
- Dominant surface + restrained accents; accent for action/state, not decoration spam
- Glass/mist panels: preserve legibility; borders soft, not muddy
- Contrast: body text readable on every background state

### Motion & feedback
- Motion supports meaning (busy rim, send affordance) — not noise
- Short, ease-out transitions; respect reduced-motion when the stack allows
- Loading/empty/error/success each have a deliberate visual state

### Details that signal craft
- Corner radii from a small set (e.g. control vs floating shell)
- Shadows soft and layered, not heavy drop-shadow soup
- Icons aligned to text cap-height; consistent stroke weight
- Focus rings visible for keyboard users

## Anti-patterns (reject these)

- Generic "AI UI": purple-on-white gradient clichés, Inter/Roboto-only everything,
  evenly mediocre spacing, identical card grids with no hierarchy
- Rainbow accents with no system; neon on neon; low-contrast gray-on-gray
- Inconsistent radii/shadows between sibling controls
- Decorative blur/glow that obscures content or brand marks
- Restyling the whole app when the ask was a single control

## Luma-specific notes (this repo)

When working in LMLB / Luma:

- Brand mark ✦; themes via `LumaTheme` + `AppSettings.UiTheme`
  - **Blue** (default): white → `#2563EB` / `#38BDF8`
  - **Colorful**: Aurora violet → cyan
- Shell: geometric parallax, mist glass, compose capsule, status pill
- Prefer edits in theme/resources and existing controls over new one-off brushes
- Brand boards in repo root (`*.png`) are reference — match live theme tokens first

## Critique format

When in Critique mode (or before large polish), report:

```
## Impeccable critique
**Intent:** …
**Strengths:** …
### Issues
1. [high|med|low] Area — problem → fix
### Recommended order
1. …
```

Then stop if critique-only; otherwise implement in that order.

## Constraints

- Prefer small, focused diffs; no drive-by refactors
- Do not remove accessibility affordances for aesthetics
- Do not invent OCR or re-add removed UI (see `AGENTS.md`)
- If a visual target is only in a screenshot, ground changes in real files —
  never claim pixels that were not inspected
