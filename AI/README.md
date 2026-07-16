# AI Context — Felova

> **For AI assistants and new developers: read this first.**
> This folder is the durable project context. It exists so you can contribute
> without reverse-engineering the codebase. Read the file that matches your task
> (map below) before writing code.
>
> **These documents describe the CURRENT state of the project — not its history.**
> They are not a changelog. Keep them small, true, and stable.

---

## How to use this folder

1. Start here, then open the one or two files relevant to your task.
2. Treat every statement as a claim you can verify in code — if a doc names a
   file, service, or table, it exists. If you find a contradiction, the code is
   the truth: fix the doc in the same change.
3. When you finish work, update these docs **only** if you changed one of:
   architecture, data flow, a domain rule, a coding convention, a project
   constraint, or an important design decision. Do **not** record bug fixes, UI
   tweaks, refactors without architectural impact, or "what I did today."

## File map

| File | Read it when you need… |
|------|------------------------|
| [project-overview.md](project-overview.md) | What Felova is, who it's for, the stack, and what's built. |
| [architecture.md](architecture.md) | How the code is organised — MVVM, DI, folders, the four subsystems, navigation, startup. |
| [domain.md](domain.md) | The data model and the domain rules that must hold (pets, conditions, care plans, entries, medications, reports). |
| [design-decisions.md](design-decisions.md) | *Why* the non-obvious choices were made — read before changing notifications, sheets, the timeline, or the report. |
| [coding-standards.md](coding-standards.md) | The conventions to follow — MVVM patterns, SQLite/migrations, localization, the shared bottom sheet, build/verify. |
| [known-constraints.md](known-constraints.md) | Platform limits, technical debt, and landmines — read before deleting code or "upgrading" a dependency. |
| [current-roadmap.md](current-roadmap.md) | What is intentionally not done yet. |

## Non-negotiable rules (the ones most likely to bite)

These are stated in full in the linked files; collected here so they're never missed.

- **Never rely on infinite OS notification recurrence.** Recurrence rules are
  expanded to bounded, concrete occurrences and re-armed on launch/boot. → [design-decisions.md](design-decisions.md)
- **Every in-app input uses the shared `FelovaBottomSheet`.** No `DisplayAlert`,
  modal pages, or hand-rolled popups for input. → [coding-standards.md](coding-standards.md)
- **Localize every user-facing string in both `AppStrings.resx` and `AppStrings.de.resx`.** → [coding-standards.md](coding-standards.md)
- **The vet report only states owner-logged facts** — no interpretation,
  severity, or advice. → [domain.md](domain.md)
- **Condition awareness lives only in the care plan.** The Journal knows
  *trackers* and *doses*, never "Diabetes". → [domain.md](domain.md)
- **QuestPDF is pinned to 2023.12.6** (last Android-compatible version). Do not upgrade. → [known-constraints.md](known-constraints.md)
