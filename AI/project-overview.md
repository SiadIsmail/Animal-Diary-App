# Project Overview

> **For AI assistants: this file answers "what is this and what's built."**
> For structure read [architecture.md](architecture.md); for rules read
> [domain.md](domain.md). Keep the status section a snapshot of the present, not
> a log — delete finished-then-obsolete lines rather than accumulating them.

---

## What Felova is

**Felova** is a .NET MAUI mobile app for owners of pets with **chronic
conditions** (diabetes, kidney disease, epilepsy, and general care). Owners log
daily health data — weight, mood, blood glucose, appetite, seizures — manage
medications with reliable reminders, review history on a calendar/journal, and
export a **vet-ready PDF** for appointments.

The emotional tone is **warm and supportive** — a caring companion, not a
clinical instrument. Copy names the pet and never judges the owner. The app
records facts; the vet does the medicine.

- **Display name / App ID:** Felova / `com.felova.app`
- **Version:** `ApplicationDisplayVersion` 1.2.2 (`ApplicationVersion` 4)
- **Platforms:** Android, iOS, MacCatalyst, Windows (Windows target only builds
  on Windows; the mobile targets are the shipping ones).
- **Stack:** .NET MAUI 9 · MVVM · SQLite (`sqlite-net-pcl`) · Microsoft DI.
- **Key packages:** `Microsoft.Maui.Controls`, `sqlite-net-pcl` (1.9.172),
  `Plugin.LocalNotification` (12.x), `QuestPDF` (pinned 2023.12.6),
  `Syncfusion.Maui.Calendar` (still referenced for `ConfigureSyncfusionCore`,
  though the calendar UI is now a custom control — see
  [known-constraints.md](known-constraints.md)).

> The German `README.md` in the repo root refers to the app as "AnimalDiary" and
> predates the Felova rebrand — treat it as a public portfolio blurb, not spec.

## Who it's for

Owners caring for a pet with a long-term condition, for whom sticky notes and
phone alarms aren't reliable enough. The product bias is a **lean, monetizable
MVP**: pet profiles, medication schedules + dose logging, symptom/weight
tracking, calendar history, reminders, PDF export, and condition tags. Don't
overbuild beyond that surface.

## Core workflows

1. **Language + onboarding** — first launch picks language (DE/EN), then Welcome
   → create a pet → optional condition setup.
2. **Today** (`MainPage`) — daily weight & mood at a glance, latest weight, a
   weight chart, and a mood timeline.
3. **Journal** (`CalendarPage`) — the day's *"Still to do"* chip row, one-tap
   dose logging, per-type input sheets, and a single chronological timeline of
   everything logged that day; a week-strip calendar to move between days.
4. **Medications** — add/edit per pet (name, dose, unit, days, 1–5 reminder
   times, notes); warm local notifications; archive/restore.
5. **Pets / Care** — switch the active pet, manage a pet (identity, conditions,
   care plan, medications), and **export the vet PDF**.
6. **Settings** — language toggle and full data reset.

## Implementation status (snapshot)

Built and working:

- Onboarding, pets, active-pet switching.
- Daily weight/mood, weight chart, mood timeline.
- Custom 7-day week-strip **calendar** with activity dots (replaced the
  Syncfusion month calendar).
- **Medications** with 1–5 dynamic reminder times; instance-based, weekday-aware
  reminders that survive reboots (Android boot catch-up); archive/restore.
- **Dose logging** (Taken / Skip) with durable adherence records and missed-dose
  reconciliation.
- **Journal rework**: pending-chip row, one-tap dosing with undo-toast, and a
  unified timeline; input sheets for **Mood, Weight, Glucose, Appetite,
  Seizure** on the shared bottom sheet.
- **Multi-condition care plans** — persisted, owner-tunable trackers; reusable
  condition setup sheets shared by onboarding and the Manage page.
- **Vet PDF export** (3-layer, offline, owner-facts-only).
- **Localization** EN + DE, switchable live at runtime.

Not yet done → see [current-roadmap.md](current-roadmap.md).
