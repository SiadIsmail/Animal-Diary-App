# Known Constraints & Technical Debt

> **For AI assistants: read this before deleting code, "upgrading" a package, or
> assuming something is missing by mistake.** These are known, accepted trade-offs
> — not TODOs to fix on sight. Touch one only when your task genuinely requires
> it, and update this file if the situation changes. Ranked roughly by how likely
> each is to bite you.

---

## Platform limits (notifications)

- **Boot catch-up is Android-only.** `BootReceiver` re-arms and re-sends missed
  doses on `BOOT_COMPLETED`. iOS has no equivalent boot hook — iOS local
  notifications survive reboot natively, but the missed-dose *re-send* only runs
  on the next app launch.
- **Aggressive OEMs** (Xiaomi/Samsung/etc.) can still kill alarms; launch/boot
  catch-up is the recovery net, but a dose missed while the device is fully off
  can't fire at its exact time — it surfaces as a "missed dose" instead.
- **Duplicate suppression** relies on the `LastSeen` heuristic; in rare timing
  windows a reminder may be re-sent once. Intentional bias — for medication a
  duplicate is safer than a silent miss. (See [design-decisions.md](design-decisions.md).)

## Dependency pins / references

- **QuestPDF is pinned to 2023.12.6** — the last Android-compatible release. Do
  not upgrade. (Rationale in [design-decisions.md](design-decisions.md).)
- **Syncfusion is still referenced** (`Syncfusion.Maui.Calendar`,
  `ConfigureSyncfusionCore` in `MauiProgram`) even though the calendar UI is now
  the custom `WeekCalendarView`. It remains for core/chart hosting; removing it is
  unverified cleanup, not a quick win.

## Legacy / dead code (verify before removing)

- **Dead inline-editor members in `CalendarViewModel`.** The Journal rework left
  several members with no XAML binding (old mood/weight inline editing, dose
  toggle commands, `DosesForSelectedDate`, etc.). Deletable as a batch **only
  after** confirming against the live binding surface (see the grep in
  [coding-standards.md](coding-standards.md)). Left in place to keep feature work
  focused.
- **`TrackingEntry` is read-only in practice.** Nothing writes it anymore (the
  removed Tracker Hub did); the **vet report still reads** it for historical
  seizure/vomiting data, so the table/service/DI must stay. Consequence: water
  intake, vomiting, sub-Q fluids, and post-seizure notes currently have **no
  logging UI** — only mood/weight/glucose/appetite/seizure do. To make them
  loggable again, add sheets (per [coding-standards.md](coding-standards.md)) —
  do not revive the hub.
- **Orphaned model vocabulary.** `TrackingItem` + `InputKind`
  (`Data/Models/Condition.cs`) are no longer produced anywhere; kept only because
  `TrackingEntry`'s doc-comments `cref` them. `MedicationTime` (an Id-only table
  created in `AppDatabase`) is likewise vestigial. Remove related items together
  if you clean this up.

## Data-model debt

- **`Pet.ConditionId` (single) vs. the `PetCondition` store (multiple).** The
  legacy single column is still maintained (primary condition). Treat the
  multi-condition store as authoritative; don't key new logic off the single id.
- **Stale data after condition removal.** Pets whose stored `ConditionId` is a
  now-removed condition (`heart`/`hyperthyroid`) fall back to "None / Not sure".
  No cleanup migration exists — intentional (migrations stay minimal).

## Presentation / styling debt

- **Two colour-token generations** in `Colors.xaml` (older hex palette +
  Rockpool). Prefer Rockpool; consolidating is unpaid debt — don't add new usages
  of the old set.
- **VM-owned presentation hints.** `TimelineItem`/`DoseItem` carry view concerns
  (tint `Color`, `IconRotation`, `CardCorner`) resolved from
  `Application.Current.Resources`. Matches existing convention but is a soft MVVM
  violation; a converter/behavior would be cleaner if it grows.

## Feature scope limits (v1)

- **Vet report is v1**: saves to `FileSystem.AppDataDirectory` only — **no share
  sheet, no preview, no range picker** (fixed 90-day look-back; the range is
  already a `GenerateAsync` parameter). Pull the file off-device via the returned
  path (also debug-logged).
- **Notification copy after boot** may use the device locale rather than the
  saved language if the `BootReceiver` runs before the language is applied — a
  minor edge case.
