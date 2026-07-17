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
- **Android exact-alarm budget.** Android caps an app at ~500 exact alarms. The
  scheduler enforces `GlobalPendingBudget` (400) across all medications when
  materializing occurrences — meds synced later in a pass get fewer instances,
  re-extended on the next launch/boot. Don't raise the budget or per-med cap
  without checking the OS limit.

## Legacy / cross-layer dependencies (verify before removing)

- **`CalendarViewModel` still carries a dose/mood/weight cluster with NO
  consumer.** The Today page's care ring and next-up card used to read
  `DosesForSelectedDate`, `HasMood`/`HasWeight` (via `ShownMood`/`ShownWeight`)
  and execute `ToggleDoseTakenCommand` from MainPage code-behind; they now
  derive from `PendingItemsService`/`PendingEngine` via `MainPageViewModel`
  instead, and nothing binds or calls those members anymore (verified across
  XAML **and** `*.xaml.cs`). They are kept only because removing them touches
  the Journal's load/NotifyDerived-dedup paths (`PrepareDataAsync`,
  `RefreshEntriesAsync`, the date setter) and deserves its own verified change —
  soft debt, safe to remove with care. Any dead-code grep must still include
  `*.xaml.cs` — see [coding-standards.md](coding-standards.md).
- **`TrackingEntry` is read-only in practice.** Nothing writes it anymore (the
  removed Tracker Hub did); the **vet report still reads** it for historical
  seizure/vomiting data, so the table/service/DI must stay. Consequence: water
  intake, vomiting, sub-Q fluids, and post-seizure notes currently have **no
  logging UI** — only mood/weight/glucose/appetite/seizure do. To make them
  loggable again, add sheets (per [coding-standards.md](coding-standards.md)) —
  do not revive the hub. Its rows key items by plain string ids ("seizure",
  "vomiting") — the old `TrackingItem`/`InputKind` vocabulary was deleted.
- **`DoseStatus.Skipped` has no writer in the current UI** (the skip command went
  with the dead Calendar checklist). Historical Skipped rows still render on the
  timeline and count in the vet report; re-adding a skip affordance is a product
  decision, not a bug.

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
  of the old set. (`WeekCalendarView`'s dot colours are the old palette's last
  code-side holdout.)
- **VM-owned presentation hints.** `TimelineItem`/`DoseItem` carry view concerns
  (tint `Color`, `IconRotation`, `CardCorner`) resolved from
  `Application.Current.Resources`. Matches existing convention but is a soft MVVM
  violation; a converter/behavior would be cleaner if it grows.

## Feature scope limits (v1)

- **Vet report export options are presets-only**: the export sheet offers
  30/90/180-day look-backs; a custom date range and per-export section toggles
  are deliberately deferred (`GenerateAsync` already takes arbitrary dates).
  Preview PNGs (~120 KB/page at 144 DPI) are stored alongside every PDF — an
  accepted disk cost; there is no auto-cleanup cap, owners delete via Documents.
  Sharing relies on MAUI Essentials' bundled Android FileProvider (no manifest
  entry of our own) — verify on-device after Essentials upgrades.
- **Notification copy after boot** may use the device locale rather than the
  saved language if the `BootReceiver` runs before the language is applied — a
  minor edge case.
