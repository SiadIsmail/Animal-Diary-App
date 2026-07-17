# Design Decisions

> **For AI assistants: read this before changing the areas it covers.** Each
> entry records a decision that is deliberate and non-obvious — reversing one
> without understanding the "because" will reintroduce a bug the project already
> solved. If you *do* change a decision, update the entry in the same change and
> reflect it in [domain.md](domain.md)/[coding-standards.md](coding-standards.md).

---

## Notifications: bounded horizon of concrete instances

**Decision:** never rely on infinite OS recurrence. Medication schedules
(recurrence *rules*) are expanded into concrete single *occurrences*
(`ReminderInstance`), only a bounded rolling horizon is scheduled as one-shot
exact notifications, and the app re-materializes + re-arms on every launch and
device boot.

**Because:** Android background limits make infinite recurrence unreliable, and
`Plugin.LocalNotification` gives no dependable background fire-callback to re-arm
from. A bounded horizon gets the same properties (exact triggers, weekday-aware,
survives reboot) that the plugin can actually deliver. Tuning knobs are constants
in `MedicationReminderScheduler` (`HorizonDays`, `MaxInstancesPerMedication`,
`HistoryRetention`).

`SyncMedicationAsync(medId)` is idempotent — it is the create path, the
edit-after-save path, **and** the "re-arm after an outcome was cleared" path
(undo of a logged dose must re-arm the reminder its logging cancelled). All
scheduler mutations are serialized behind one `SemaphoreSlim` gate (launch/boot
catch-up vs. user saves would otherwise interleave and duplicate notifications),
and materialization respects a `GlobalPendingBudget` so the app stays under
Android's exact-alarm cap. `NotificationIds` are deterministic (per type-range
and per instance) so update/cancel work; never randomize them.

## `resendMissed`: boot re-sends, launch does not

`CatchUpAndRefreshAsync(bool resendMissed)` re-arms future reminders both on
launch and boot, but the flag differs:

- **Boot** (`resendMissed: true`): the device was off, so doses in the off window
  never fired and must be re-sent (coalesced per medication, skipping doses
  already logged taken/skipped).
- **Launch** (`resendMissed: false`): the device was on, so the OS already
  delivered those notifications — re-sending would spam duplicates every time the
  app opens.

The app is normally *closed* when reminders fire, so on launch most past
instances were already delivered; genuine device-off gaps always come back
through a reboot, which the boot receiver handles. The `LastSeen` marker
(`Preferences`) distinguishes the off-window. We bias toward re-sending on
genuine gaps — for medication a duplicate is safer than a silent miss.

## One reusable bottom sheet for every input

**Decision:** every in-app input slides up in the single `FelovaBottomSheet`
control — the medication add/edit form and every Journal sheet use the *same*
control. Never hand-roll popup/slide/scrim logic, and never use `DisplayAlert`,
modal pages, or full-page navigation for input.

**Because:** it guarantees one consistent feel (motion, scrim, drag handle,
reduced-motion support) and one place to fix behaviour. The one-tap dose logging
path uses a **6-second undo-toast** as its safety net — that is the required
alternative to a confirmation dialog for medical logging; do not replace it with
a modal confirm. The sheet's overlay-hosting `InputTransparent` rule is a real
landmine — see [coding-standards.md](coding-standards.md).

## Persisted, owner-tunable care plans (seeded once)

**Decision:** the care plan is stored as `Tracker` rows, seeded from
`CarePlanCatalog` on first access and then owned by the user, rather than being
recomputed from conditions each time.

**Because:** owners tune what they track (cadence, targets, turning things off);
that intent must survive and must not be stomped by a re-seed. `CarePlanService`
hides the seed-once + condition-merge behind one async call so callers stay
naive. See [domain.md](domain.md).

## Condition awareness lives only in the care plan

**Decision:** the Journal knows *trackers* and *doses* only — the word
"Diabetes" never appears in Journal UI or code paths. Conditions influence the
Journal solely by shaping the seeded care plan.

**Because:** it keeps the logging surface generic and open to new conditions
without touching Journal code, and keeps disease vocabulary out of a warm,
non-clinical UI.

## One unified timeline

**Decision:** the Journal day view is a single chronological
`ObservableCollection<TimelineItem>` across all kinds, sorted purely by time — no
per-kind sections, no per-type sort.

**Because:** owners think in "what happened today, in order," not in
per-metric buckets. (This replaced earlier per-kind timeline collections.) A
template selector varies the card; the ordering rule is a requirement, not
incidental.

## Journal reloads are deduped by (pet, date) context

**Decision:** `CalendarViewModel.NotifyDerived()` raises `ActivePetName` on
*every* entries/doses load, not only on a real pet switch — so `CalendarPage`
keeps a "last-reloaded (petId, date)" marker and only reloads the Journal when
that context actually changed; explicit post-save reloads always run and stamp
the marker.

**Because:** listening to `ActivePetName` naively cascaded 2–4 redundant
full-day gathers on every save/appearance (reload → refresh → NotifyDerived →
reload…). If you add a new trigger for `JournalLogViewModel.ReloadAsync`, route
it through the page's marker, not a raw PropertyChanged subscription.

## New VMs/services over reshaping old ones

**Decision:** the Journal rework and Manage-pet work added new VMs
(`JournalLogViewModel`, sheet VMs, `ManagePetViewModel`) instead of bloating
`CalendarViewModel`.

**Because:** the older `CalendarViewModel` still drives the week strip and day
headings; growing it would entangle two designs. (Its dead inline-editor members
have since been removed. The Today page's care ring and next-up card no longer
read it either — they derive from the same `PendingEngine` snapshot as the
Journal's chips, via `PendingItemsService.GetTodayCareAsync` on
`MainPageViewModel` — leaving a now-unconsumed dose/mood/weight cluster listed in
[known-constraints.md](known-constraints.md).)

## Custom `WeekCalendarView` replaced the Syncfusion calendar

**Decision:** the calendar is a custom pure-UI `ContentView` (bindable
`SelectedDate`/`Activities`, Monday-based week strip, activity dots), not
`Syncfusion.Maui.Calendar`.

**Because:** the Syncfusion month-cell customization needed for activity dots was
a blocker; a purpose-built strip is simpler and fully themeable. All scheduling
logic stays in the VM; the control owns no business logic. (The Syncfusion
package has been removed entirely — nothing referenced it.)

## Vet report: three independent layers

**Decision:** the report is split DATA (`VetReportData` + `VetReportDataBuilder`,
the only class touching SQLite) → DOCUMENT (`Document/`: independent
`IVetReportSection` classes, all layout constants in `VetReportStyles`, order =
the `Sections` array in `VetReportDocument`) → SERVICE
(`IVetReportService.GenerateAsync` → file path or null).

**Because:** the layout will be redesigned repeatedly after vet feedback, so
sections must stay independent of each other *and* of data access. The document
layer has **no MAUI dependency**, which enables a console harness (+
`VetReportSampleData`) to iterate on layout without a device or real data.

## Report preview = PNGs rendered at generation time

**Decision:** every export saves the PDF **and** one preview PNG per page
(`{name}.p{n}.png`, `VetReportStyles.PreviewRasterDpi` = 144) into `Reports/`,
tracked by a `VetReportFile` row (`ReportLibraryService` owns rows + files). The
in-app viewer (`ReportPreviewPage`) and the Documents thumbnails show those
images; Share / "Open with…" hand the PDF to the OS via `ReportActions`.

**Because:** Android WebView cannot render PDFs and QuestPDF cannot re-read a
saved one — generation time is the only moment the document object exists, so
pre-rendering there buys an instant, offline, cross-platform preview with no
platform PdfRenderer code. 144 DPI keeps a page ~120 KB (288 default was
multi-MB). Trade-off: previews cost disk alongside each PDF; delete/reset
removes both.

## Deletion uses the undo toast, not a confirm dialog

**Decision:** deleting a report from Documents follows the app's undo-toast
pattern (now the shared `Controls/UndoToast`, extracted from CalendarPage): the
row disappears immediately, actual file+row deletion is **deferred** until the
toast expires or the page is left; Undo restores the row. The toast's
`expiredAsync` callback exists precisely for this deferred-commit contract; a
superseded toast calls neither callback, so commits must stay idempotent.

**Because:** confirmation dialogs are deliberately avoided app-wide (see the
bottom-sheet entry above); undo-after-the-fact is the established safety net.

## QuestPDF pinned to 2023.12.6

**Decision / because:** 2023.12.6 is the **last** QuestPDF version that runs on
Android (2024.3+ ships no Android native Skia binaries). Do not "upgrade" it. The
Community license (free under $1M USD revenue) is declared once in
`VetReportService`'s static constructor.
