# Felova — AI Context Addendum (Journal timeline, conditions, doses, tech debt)

**Read [AI_CONTEXT.md](AI_CONTEXT.md) first** for the full architecture (MVVM + DI,
notifications, medications, localization). This addendum records a later rework and
the **current technical debt**, and lists which parts of the main doc are now stale.

> Last updated: 2026-07. If you change these areas, update this file in the same commit.
> When the main `AI_CONTEXT.md` is next revised, fold these corrections back into it
> and delete the "superseded" list below.

---

## 0. Sections of AI_CONTEXT.md that are now SUPERSEDED

The main doc predates this rework. Trust **this** file where they disagree:

| AI_CONTEXT.md says | Reality now |
|---|---|
| §6.4 timeline = "glucose/appetite timeline collections" | One **unified** `TimelineItems` list (mood+weight+glucose+appetite+seizure+doses), sorted purely by time. |
| §6.5 "Seizure '+' is a stub toast" | Seizure has a **full sheet** and logs `SeizureEntry`. |
| §6.5 "new Journal strings are interim English literals `TODO(Step 5)`" | All Journal + condition strings are localized **EN + DE**. |
| §6.1 "Tracker Hub" / §6 "Today's Tracking … Tracker Hub" | The Tracker Hub is **removed** (dead code deleted). |
| Appetite "int Level … reading" implying many/day | Appetite is **one reading per day** (replace-on-relog, like mood/weight). |
| Conditions incl. heart / hyperthyroid (older text) | Only **None, Diabetes, CKD, Epilepsy** remain. |
| `CalendarViewModel.LoadDosesAsync` builds doses inline | Dose day-assembly is centralized in `DayDoseService`. |
| §8 Colors "purple `#9B6FCC`…" | That's the older palette; the Journal uses the Rockpool tokens (`Glass`, `*Tint`, `Teal*`, `PaperWarm`, `Washi`, `NoteInk`). Two palettes coexist (debt #4). |

---

## 1. The Journal timeline (current design)

The Journal is `CalendarPage`, driven by **two** VMs:
- **`CalendarViewModel`** — pet selector, week strip / month dots (`WeekActivities`),
  day headings. Older, pre-rework.
- **`JournalLogViewModel`** (`MainViewModel.JournalVM`) — the "Still to do" chip row
  **and** the single chronological timeline. This is where new Journal work goes.

**Single chronological ordering (requirement, not incidental):**
`JournalLogViewModel.GatherTimelineAsync` builds **one**
`ObservableCollection<TimelineItem>` from every kind — mood, weight, glucose,
appetite, seizures, doses — then `OrderBy(i => i.Time ?? TimeSpan.Zero)`. There is
**no per-kind section and no separate sort per type**; do not reintroduce either.
- Every item shows its recorded time. Legacy mood/weight rows with **no** stored
  time (`PetEntry.MoodTimeTicks`/`WeightTimeTicks` null) sort at start-of-day and
  hide the time label.
- **Doses are placed at the moment they were tapped** (`MedicationDoseLog.ResolvedAt`),
  falling back to scheduled time when not yet acted on.
- Rendering: one `BindableLayout` + `TimelineTemplateSelector` chooses Mood
  (washi-note card) vs. the standard card. Adding a kind = extend `TimelineKind`,
  emit items in the gatherer, and add a template only if the layout differs.
- `TimelineItem` carries presentation hints (`Tint` `Color`, `IconRotation`) resolved
  from `Application.Current.Resources` — see debt #7.

## 2. Input sheets — the uniform contract

Every log type = `*SheetViewModel` + `*SheetView.xaml` in the shared
`FelovaBottomSheet`. To add one, copy **`SeizureSheetView(Model)`** or
**`AppetiteSheetView(Model)`** (cleanest examples) and wire all six touch-points:
1. VM: `OpenAsync(petId, petName, date)`, `IsPresented/Title/Subtitle/SaveCommand/DismissCommand`,
   and `event Action<JournalSaveResult>? Saved` (message **+ undo delegate**).
2. Register the VM in `MauiProgram` and expose it on `MainViewModel`.
3. Declare `<views:XSheetView … BindingContext="{Binding XSheetVM}"/>` in `CalendarPage.xaml`.
4. Subscribe/unsubscribe `Saved` in `CalendarPage.xaml.cs` (`OnSheetSaved`).
5. Add a case in `CalendarPage.OpenSheetForKindAsync`.
6. If it belongs under "+", add it in `JournalLogViewModel.BuildAddOptionsAsync`
   (gated on the pet's care plan — e.g. Seizure only shows when the plan has a
   Seizure tracker, i.e. epilepsy).

**One-per-day vs. event:** Mood, Weight, **Appetite** are one row per day — the sheet
*replaces* it (undo restores the previous value; see `AppetiteSheetViewModel.SaveAsync`
and `AppetiteEntryService.UpdateAsync`). Glucose and Seizure are events — each save
inserts a new row.

## 3. Doses — the single join (`DayDoseService`)

`DayDoseService.GetForDayAsync(petId, date)` is the **one** place that expands a
pet's schedules for a day and joins the dose logs, returning
`DayDose(Medication, ScheduledTime, Log?)`. Three callers project it:
`PendingItemsService` → `ScheduledDose`; `JournalLogViewModel` → `TimelineItem`;
`CalendarViewModel.LoadDosesAsync` → `DoseItem`. **Do not re-inline this join.**
(Whole-**week** dot expansion is different and stays in `CalendarViewModel` via
`MedicationScheduleExpander`.)

## 4. Conditions (current)

- **`CarePlanCatalog`** seeds persisted `Tracker` rows from a pet's condition(s) —
  the live care-plan model. **`ConditionCatalog`** is now just the condition list +
  `GetCondition`; names are localization keys resolved by `Condition.Name`
  (`Condition_None/Diabetes/Ckd/Epilepsy`).
- Supported: **None, Diabetes, CKD, Epilepsy/Seizures.** Configurable ones (with a
  setup sheet) are listed in `ConditionSetup` (diabetes/ckd/epilepsy).

---

## 5. Technical debt & landmines (ranked by likelihood of biting you)

1. **Dead legacy inline-editor plumbing in `CalendarViewModel`.** The chip-row +
   sheet rework left these members with **no XAML binding**: `EnteredMood`,
   `SelectedMoodLevel`, `ShownMood/ShownWeight`, `ShownMoodDisplay/ShownWeightDisplay`,
   `HasMood/HasWeight`, `MoodNarrative`, `AllCareComplete`,
   `EntrySection/MoodSection/WeightSection/MedicationSection`, `ShowMoodInputCommand`,
   `OnMood/WeightEntryCompleted`, `SelectMoodCommand`, `ToggleDoseTakenCommand`,
   `SkipDoseCommand`, and `DosesForSelectedDate` itself. Deletable as a batch —
   **verify against the live binding surface first** (see §6). Left in place to keep
   the feature work focused.

2. **`TrackingEntry` is now read-only in practice.** Nothing writes it anymore (the
   Tracker Hub that did was removed); the **vet report still reads** it for historical
   data (e.g. `ItemId == "seizure"`), so the table/service/DI must stay. Consequence:
   water intake, vomiting, sub-Q fluids and post-seizure notes have **no logging UI**
   now — only mood/weight/glucose/appetite/seizure do. If they must be loggable again,
   add sheets (§2), don't revive the hub.

3. **Orphaned model vocabulary.** `TrackingItem` + `InputKind`
   (`Data/Models/Condition.cs`) are no longer produced anywhere (the
   `ConditionCatalog.Items` catalog was removed). Kept only because `TrackingEntry`'s
   doc-comments `cref` them. Remove all three together if you want them gone.

4. **Two colour-token generations in `Resources/Styles/Colors.xaml`** (older palette +
   Rockpool). Prefer the Rockpool tokens the Journal already uses; consolidating is
   unpaid debt.

5. **`Pet.ConditionId` (single) vs. the `PetCondition` multi-condition store.** The
   legacy single column is still maintained (pointed at the primary). Treat the
   multi-condition store as authoritative; don't add new logic keyed off the single id.

6. **Stale data after condition removal.** Pets whose stored `ConditionId` is a
   removed condition (`heart`/`hyperthyroid`) fall back to "None / Not sure". No
   cleanup migration was written (intentional — keep migrations minimal).

7. **VM-owned presentation hints.** `TimelineItem`/`DoseItem` carry view concerns
   (`Color` tint, `IconRotation`, `CardCorner`) resolved from `Application.Current.Resources`.
   Matches existing convention but is a soft MVVM violation; a converter/behavior
   would be cleaner if this grows.

8. **`AI_CONTEXT.md` is partly stale** (see §0). Fold these corrections in and delete
   this addendum when convenient.

---

## 6. Working conventions for these areas

- **Find the live UI surface before deleting a VM member:**
  `grep -rhoE 'CalendarVM\.[A-Za-z0-9_]+' "Animal Diary App/Data/View/"*.xaml | sort -u`
  (repeat per VM: `JournalVM.`, etc.). Unbound + unused-by-other-members = dead.
- **Build the Windows target to check compiles (fastest local target):**
  `dotnet build "Animal Diary App/Animal Diary App.csproj" -f net9.0-windows10.0.19041.0 -c Debug -clp:ErrorsOnly`
  Expect ~0 errors and a large **pre-existing** warning count — don't chase it.
- **Additive model changes need no migration** (SQLite-net auto-adds columns); a new
  **table** must be added to `AppDatabase.InitAsync`.
- **Localize every user-facing string** in both `AppStrings.resx` and `.de.resx`.
- **Smallest-change ethos:** extend existing models/services over adding new ones;
  when adding, mirror the nearest existing example. Match the repo's high comment density.
