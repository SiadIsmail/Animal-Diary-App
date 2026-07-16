# Domain Model & Rules

> **For AI assistants: this file is the source of truth for the data model and
> the invariants that must hold.** Break one of the rules at the bottom and the
> app is wrong even if it compiles. For *why* the model has this shape read
> [design-decisions.md](design-decisions.md). Update this file when a table,
> relationship, or domain rule changes.

---

## Persistence basics

SQLite via `AppDatabase` (`Connection` + `EnsureInitializedAsync()`). Every table
is created once in `AppDatabase.InitAsync`. Entities use
`[PrimaryKey, AutoIncrement] int Id`. `sqlite-net` **auto-adds new columns** for
new properties but never drops columns — additive changes need no migration; a
new **table** must be added to `InitAsync`. Enums that persist declare
`[StoreAsText]` **on the enum type**, not the property (1.9.172 requirement).

## Tables

| Table | Key fields | Relationship / note |
|-------|-----------|---------------------|
| `Pet` | Id, Name, Type, Age, `ConditionId` (legacy) | root entity |
| `PetEntry` | Id, PetId, Date, Weight, Mood, MoodNote, `MoodTimeTicks`/`WeightTimeTicks` | Pet 1—* ; **one row per pet per day** (mood/weight replace-on-relog) |
| `Medication` | Id, PetId, Name, Dosage, Unit, Notes, IsArchived, `CreatedAt` | Pet 1—* |
| `MedicationSchedule` | Id, MedicationId, Day (`DayOfWeek`), Time (`TimeSpan`) | recurrence **rule**: one row per (day × time) |
| `ReminderInstance` | Id, MedicationId, ScheduledTime, NotificationId, Status, SlotIndex | materialized **occurrence** of a rule |
| `MedicationDoseLog` | Id, MedicationId, PetId, ScheduledDate, ScheduledTime, Status, ResolvedAt | durable adherence record per dose |
| `Tracker` | Id, PetId, TrackerId, Kind, PerDayCount, TargetLo/Hi, Unit, FromCondition | the pet's **care plan** (persisted) |
| `PetCondition` | Id, PetId, ConditionId | Pet 1—* conditions (**multiple** per pet) |
| `GlucoseEntry` | Id, PetId, Date, Time, Value, FoodContext | Pet 1—* ; **many per day**, event, never upserted |
| `AppetiteEntry` | Id, PetId, Date, Time, Level | Pet 1—* ; **one per day** (replace-on-relog) |
| `SeizureEntry` | Id, PetId, Date, Time, DurationMinutes, Note | Pet 1—* ; event, never upserted |
| `TrackingEntry` | generic legacy tracking rows | **read-only now** (see below + [known-constraints.md](known-constraints.md)) |
| `AppSettings` | preferences incl. `Language` | singleton-ish |

## Conditions

- Supported set: **None, Diabetes, CKD, Epilepsy/Seizures.** Defined in
  `ConditionCatalog` (list + `GetCondition`); names are localization keys
  (`Condition_None/Diabetes/Ckd/Epilepsy`) resolved by `Condition.Name`.
- A pet can have **multiple** conditions, stored in `PetCondition` via
  `PetConditionService`. This store is **authoritative**.
- `Pet.ConditionId` is a **legacy single column**, still maintained (pointed at
  the primary condition) and migrated into a `PetCondition` row on first read.
  Don't key new logic off it.
- **Configurable** conditions (those with a setup sheet) are diabetes / ckd /
  epilepsy, listed in `ConditionSetup` (`HasSheet` is the single source of truth).

## Care plan = trackers

A pet's **care plan** is a list of `Tracker` rows — the things the Journal asks
for. It is **persisted and owner-tunable**, not derived on the fly.

- A `Tracker` has a `TrackerId` (Glucose | Mood | Appetite | Weight | Water |
  Seizure), a `TrackerKind` cadence (PerDay | Daily | Weekly | TwiceWeekly |
  AsNeeded | Event), `PerDayCount`, optional glucose `TargetLo/TargetHi`
  (+ `TargetRange`), `Unit`, and `FromCondition` (breadcrumb only).
- **`CarePlanCatalog.BuildDefaultPlan(condition[s])`** is the **seed only**:
  defaults **Mood (Daily) + Weight (Weekly)**, plus each condition's extras
  (diabetes → Glucose PerDay + mmol/L target; ckd → Appetite + Water; epilepsy →
  Seizure), merged and de-duped across conditions.
- **`CarePlanService.GetPlanAsync(pet)`** is the single async seam the Journal
  uses. It reads persisted `Tracker` rows (`TrackerService`), **seeding once**
  from the catalog on first access (guarded so a tuned plan is never re-seeded or
  duplicated), and merges the pet's conditions from `PetConditionService`. The
  Journal only ever asks "what is this pet's plan?" — persistence and the
  condition merge are invisible to it.

## Entry stores

- **Mood & Weight** live on `PetEntry`, **one row per day** — logging replaces
  the day's value (undo restores the previous). Both carry an optional recorded
  time; legacy rows without one sort at start-of-day.
- **Glucose** (`GlucoseEntry`) and **Seizure** (`SeizureEntry`) are **events** —
  each save inserts a new row; many per day; never upserted.
- **Appetite** (`AppetiteEntry`) is **one reading per day** (replace-on-relog).
  `Level` is 1–5, displayed as a word via `AppetiteLevel` (never "3/5").
- **Doses** are `MedicationDoseLog` rows — see below.

## Medications: rules → instances → logs

Three distinct concepts; do not collapse them.

1. **Rules** — `MedicationSchedule` rows (weekday + time). User-edited source of
   truth. A med taken twice a day on Mon/Wed has 4 rows; the distinct times are
   the daily reminder set, and their count is "times per day" (max 5).
2. **Instances** — `ReminderInstance` rows: one concrete local-wall-clock
   `ScheduledTime` per occurrence, with a `Status`
   (Pending/Fired/Missed/Cancelled). Materialized over a bounded rolling horizon
   and scheduled as one-shot OS notifications. Never schedule rules with the OS
   directly. (See [design-decisions.md](design-decisions.md).)
3. **Dose logs** — `MedicationDoseLog` rows record adherence
   (Taken/Skipped/Missed) per (medication, date, time), with `ResolvedAt`. A dose
   is "given" once **any** log row exists for it (so a skip doesn't nag).

**`DayDoseService.GetForDayAsync(petId, date)` is the one place** that expands a
pet's schedules for a day and joins the dose logs. Three callers project its
result — `PendingItemsService`, `JournalLogViewModel`, `CalendarViewModel`.
Do not re-inline this join. (Whole-**week** dot expansion is separate, in
`CalendarViewModel` via `MedicationScheduleExpander`.)

## Journal domain rules

- **Pending** ("Still to do today") is computed by the **pure**
  `PendingEngine.Compute(...)`; `PendingItemsService` gathers its inputs.
  Ordering: ungiven med doses first (soonest time), then PerDay while count <
  target, then Daily, Weekly (rolling 7 days), TwiceWeekly (rolling 3 days).
  AsNeeded/Event are never pending. All of today's ungiven doses show regardless
  of clock time.
- **One timeline, one sort.** `JournalLogViewModel.GatherTimelineAsync` builds a
  **single** `ObservableCollection<TimelineItem>` from every kind (mood, weight,
  glucose, appetite, seizure, doses) and orders purely by time. There is **no
  per-kind section and no separate sort per type** — do not reintroduce either.
  Doses sit at the moment they were tapped (`ResolvedAt`), falling back to
  scheduled time.

## Vet report rules

- The report **reports owner-logged facts only.** Nothing interprets, flags,
  scores severity, or recommends. `VetReportData` is a presentation-free
  snapshot; the document layer decides wording. The footer disclaimer
  ("Owner-reported data · Not a medical record") must stay.
- Counts are facts (rows counted). Weight change is stated only when the range
  itself holds ≥2 readings — never inferred across gaps.
- Seizures are read from **both** `SeizureEntry` and legacy `TrackingEntry`
  "seizure" rows until that data is migrated.
- An empty range produces **no** document (`HasAnyData` gate → `GenerateAsync`
  returns null).

## Invariants (must always hold)

- Mood, Weight, Appetite → **one row per pet per day**. Glucose, Seizure →
  **events, append-only**.
- The `PetCondition` store is authoritative for conditions; `Pet.ConditionId` is
  legacy.
- Care plans are **persisted** and seeded **once**; never re-seed a tuned plan.
- Condition awareness is confined to the care plan — Journal code/UI speaks only
  of *trackers* and *doses*, never a disease name.
- Reminder rules are never handed to the OS directly; only bounded, concrete
  instances are.
- Stored data is never translated (pet types/moods are canonical keys localized
  only for display; pet/med names are verbatim user data). See
  [coding-standards.md](coding-standards.md).
