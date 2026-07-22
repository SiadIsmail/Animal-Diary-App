# Felova — Chronic Pet Care Tracker

A .NET MAUI app for owners of pets with chronic conditions (diabetes, kidney
disease, epilepsy). Log weight, mood, blood glucose, appetite, and seizures;
manage medications with reliable reminders; review everything on a calendar
journal; and export a vet-ready PDF for appointments.

**Private by default.** The app works fully offline with no account — all data
stays in a local SQLite database. Cloud backup and multi-caregiver sharing are
strictly opt-in: nothing leaves the device until the owner creates an account
*and* explicitly turns backup on.

## Features

- Pet profiles with multi-condition care plans (persisted, owner-tunable trackers)
- Medication schedules with exact, reboot-surviving local reminders and durable
  dose-adherence records
- Daily journal: one-tap dose logging, per-type input sheets, a single
  chronological timeline, and a "still to do" chip row
- Vet PDF export — strictly owner-logged facts, never interpretation
- Optional cloud: account + backup, multi-device sync, and caregiver sharing
  via single-use invite codes (Supabase / PostgreSQL, offline-first
  last-write-wins sync)
- English + German, switchable live at runtime

## Tech stack

| Layer | Choice |
|---|---|
| UI | .NET MAUI 9 (XAML), MVVM, Microsoft DI |
| Local data | SQLite (`sqlite-net-pcl`) — the on-device source of truth |
| Reminders | `Plugin.LocalNotification`, bounded materialized occurrences |
| PDF | QuestPDF **pinned to 2023.12.6** (last Android-compatible release — do not upgrade) |
| Cloud (optional) | Supabase (PostgreSQL, GoTrue auth, RLS) over hand-built HTTP — no SDK |
| Analytics | Privacy-first, anonymous PostHog capture built by hand — no SDK |

## Architecture in one paragraph

UI binds to ViewModels, ViewModels call services, services own SQLite and the
device APIs — nothing above a service touches the database. Five service
subsystems: **Data** (repositories), **Notifications** (schedules expanded into
bounded concrete reminder instances), **Journal** (care plan + pure pending
engine + typed entry stores), **Reports** (three-layer vet PDF with no MAUI
dependency in the document layer), and **Cloud** (an optional sync layer behind
`ICloudSyncService`; when disabled, a null implementation is registered and the
app carries zero cloud behaviour). Sync is pull→apply→push against Postgres
with client-generated GUID identities, soft-delete tombstones, and
last-write-wins conflict resolution; the cloud design is documented in
[CLOUD_SYNC_PLAN.md](CLOUD_SYNC_PLAN.md).

## Getting started

Prerequisites: .NET 9 SDK with the MAUI workload (`dotnet workload install maui`).

```bash
git clone <this repo>
cd Animal-Diary-App

# Fastest compile check (Windows)
dotnet build "Animal Diary App/Animal Diary App.csproj" -f net9.0-windows10.0.19041.0 -c Debug

# Android
dotnet build "Animal Diary App/Animal Diary App.csproj" -f net9.0-android -c Debug
```

The app runs fully without any backend. To develop the optional cloud
features, create a Supabase project and follow
[supabase/README.md](supabase/README.md) (run the SQL migrations in order,
switch the auth email templates to code-based confirmation), then point
`Data/Services/Cloud/CloudConfig.cs` at your project URL and publishable key.

Release Android builds are signed via an untracked `keystore.props`
(`Animal Diary App/keystore.props`) providing the keystore passwords; the
build works without it for debug.

## Rules that keep the app correct

These conventions are load-bearing — breaking one produces a subtly wrong app
even if it compiles:

- **A clean build has 0 errors and ~40 known warnings** (XamlC `XC0025` notes
  plus two intentional `CS0162` from `const` feature switches). A warning you
  didn't expect is a signal.
- **Every in-app input uses the shared `FelovaBottomSheet`** — no dialogs,
  modals, or hand-rolled popups. Destructive actions use the undo-toast
  pattern, not confirmation prompts.
- **Every user-facing string is localized in both** `AppStrings.resx` (EN) and
  `AppStrings.de.resx` (DE). Stored data is never translated.
- **Synced entities carry sync columns**: every repository write goes through
  `SyncStamp.Touch`/`MarkDeleted`, deletes are soft (tombstones), and every
  read filters `IsDeleted == false`. A new table must be added to
  `AppDatabase.InitAsync` *and* `AppResetService.ResetDataAsync` in the same
  change — a data reset must wipe everything.
- **Never hand recurrence rules to the OS.** Medication schedules are expanded
  into bounded concrete occurrences and re-armed on launch/boot.
- **Felova records; it never judges.** No surface rates, flags, colours, or
  advises on health data — a weight change is a neutral signed fact. The vet
  report states owner-logged facts only.
- **Cloud/server failures must name themselves** — server functions raise
  explicit named errors; never let a write die as a bare RLS denial.
- **Analytics is anonymous and data-minimized**: event properties describe the
  event, never the user, and never carry names, notes, or medical detail.

## Contributing

Match the surrounding code: high comment density explaining *why*, smallest
change over new abstractions, new features as new ViewModels/services rather
than reshaping existing ones, and every new service/VM registered in
`MauiProgram`. Verify with the Windows compile check above before submitting;
if your change touches medication logging or reminders, test the undo paths —
they are the app's safety net for medical data.

## Status

Actively developed (work in progress). The mobile targets (Android first) are
the shipping ones; Windows/macOS builds are used for development.
