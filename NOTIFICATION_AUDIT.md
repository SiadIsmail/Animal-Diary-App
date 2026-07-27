# Notification System Audit — Felova

**Date:** 2026-07-27
**Scope:** the whole local-notification subsystem — scheduling, permissions, persistence,
cancellation, recurrence, catch-up/recovery, platform receivers, and the ViewModels that
drive them.
**Method:** full read of every file in `Data/Services/Notifications/`,
`Data/Services/Data/Device/`, `Platforms/Android/`, and all call sites; cross-checked
against the **actual v12.0.1 source of Plugin.LocalNotification** (the shipped pin is
12.0.2) and against Android/Apple platform documentation.
**Constraint honoured:** no code was changed.

---

## 1. Verdict

**Not production-ready as-is for a medication-reminder product, but close.** The
architecture is right — the bounded-horizon, concrete-instance design in
`AI/design-decisions.md` is the correct answer to Android's background limits, and it is
implemented cleanly and idempotently. What is missing is the layer *below* the
architecture: the app never verifies that the OS actually accepted a notification, and
four separate paths can silently destroy the missed-dose guarantee that the whole design
exists to provide.

Ranked summary:

| # | Finding | Severity |
|---|---------|----------|
| H1 | Scheduling failures are invisible — `Show()`'s result is discarded | **Critical** |
| H2 | Notification permission is requested from exactly one place, after the fact | **Critical** |
| H3 | Boot startup races the boot receiver and can swallow every missed dose | **Critical** |
| H4 | `ClearPendingAsync` deletes past-due instances and dismisses live notifications | **High** |
| H5 | Missed-dose re-sends are silently dropped when boot catch-up is slow | **High** |
| H6 | Boot recovery does far more work than a receiver's ~10 s budget allows | **High** |
| H7 | iOS: the 400-instance budget is 6× the OS cap of 64 | **High** (iOS blocker) |
| H8 | `LastSeen` means "last catch-up", so a reboot sprays false missed-dose alerts | **High** |
| H9 | The reconciler writes clinical "Missed" facts the owner never logged | **High** |
| M1 | One channel named "General" at `IMPORTANCE_DEFAULT` for everything | Medium |
| M2 | No `CATEGORY_REMINDER` — Do Not Disturb can't be configured to let doses through | Medium |
| M3 | Every launch tears down and rebuilds all ~400 alarms, O(n²) in the plugin's store | Medium |
| M4 | No `MY_PACKAGE_REPLACED` receiver — reminders may die on every Play auto-update | Medium |
| M5 | Time-zone travel marks un-fired reminders `Fired` and manufactures missed doses | Medium |
| M6 | Delivery drift under Doze is larger than the docs claim | Medium |
| M7 | `NotificationContent.Recurrence` defaults to `Daily` — a footgun against rule #1 | Medium |
| M8 | Boot-armed notification copy can be in the wrong language | Medium |
| L1–L7 | Dead code, missing tests, error handling, naming | Low |

---

## 2. How the system works (as built)

Three schedulers sit behind one device boundary:

- **`MedicationReminderScheduler`** — expands `MedicationSchedule` rules into concrete
  `ReminderInstance` rows over a 14-day horizon (`HorizonDays`), max 70 per medication,
  400 globally, and arms each as a one-shot. All mutations serialize behind one
  `SemaphoreSlim`. `CatchUpAndRefreshAsync(resendMissed)` runs on launch (`false`), boot
  (`true`), and clock/time-zone changes (`false`).
- **`DailyCareReminderScheduler`** — at most one *silent* notification per pet per day,
  armed only for today and only when `PendingItemsService` says something is due.
- **`MedicationDoseReconciler`** — sweeps 14 days back and writes durable
  `DoseStatus.Missed` rows for scheduled doses with no log.

`INotificationService` → `NotificationService` → `Plugin.LocalNotification` is the only
plugin contact point. Android adds `BootReceiver` and `TimeChangeReceiver`, both routed
through `ReminderRecovery.Run`.

---

## 3. Critical findings

### H1 — Scheduling failures are invisible; the app records "armed" for notifications the OS never received

**Evidence.** [NotificationsService.cs:47](Animal%20Diary%20App/Data/Services/Data/Device/NotificationsService.cs#L47):

```csharp
await LocalNotificationCenter.Current.Show(request);   // returns bool — discarded
```

`ScheduleNotification` returns `Task`, not `Task<bool>`, so the result cannot even be
propagated. In the plugin's Android implementation, `Show()` **returns `false` without
scheduling anything** in two cases:

1. `AreNotificationsEnabled()` is false — i.e. `POST_NOTIFICATIONS` was never granted, or
   the user switched notifications off in system settings. It logs `"Notifications are
   disabled"` and returns.
2. `ShowLater()` rejects the request — stale `NotifyTime` (see H5) or a null alarm intent.

Meanwhile [MedicationReminderScheduler.cs:170–189](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L170-L189) inserts every
instance with `Status = Pending` regardless, and the next catch-up
([:325](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L325))
stamps it `Fired`. The database asserts delivery for something that never existed.

**User impact.** A carer who declines the permission prompt — the *default* state on
Android 13+ — gets **zero medication reminders, forever, with no indication anywhere in
the app**. Worse, because instances are marked `Fired`, even the boot-time missed-dose
re-send is suppressed: the app concludes those doses were already delivered. The single
failure mode the design is meant to prevent (a silent miss) becomes the default outcome
of one tap on a system dialog.
**Best practice.** Android's
[notification runtime permission guide](https://developer.android.com/develop/ui/views/notifications/notification-permission)
requires apps to check `areNotificationsEnabled()` and handle the denied state — a
runtime permission that can be revoked at any time cannot be treated as a one-time
setup step.

---

### H2 — Permission is requested from exactly one place, after the save, with no rationale and no recovery

**Evidence.** `RequestPermissionAsync()` has exactly one call site in the entire codebase —
[MedicationViewModel.cs:502](Animal%20Diary%20App/Data/ViewModels/MedicationViewModel.cs#L502), and it runs *after*
the medication is persisted and the sheet has already closed:

```csharp
var permissionGranted = await _reminderScheduler.RequestPermissionAsync();
if (!permissionGranted)
    System.Diagnostics.Debug.WriteLine("[MedicationViewModel] Notification permission was not granted; …");
```

Nothing else in the app — not onboarding, not `App.StartAsync`, not the Settings panel —
ever asks or re-checks.

**User impact, three distinct scenarios.**

1. **Daily care reminder is dead on arrival for many users.** The Settings toggle
   ([SettingsViewModel.cs:152–163](Animal%20Diary%20App/Data/ViewModels/SettingsViewModel.cs#L152-L163))
   calls `RefreshAsync()` but never requests permission. A user with no medications who
   turns the feature on gets a UI that says it's enabled and a notification that can
   never fire.
2. **Denial is permanent and unexplained.** Android 13+ treats two dismissals as a
   standing denial; the system dialog then stops appearing entirely. The app has no path
   to system settings and never mentions the problem.
3. **Revocation is undetected.** A user who turns notifications off months later gets
   the H1 outcome with no signal.

**Best practice.** Android's guidance is to request in context, immediately after the
user opts into something that needs it, and to precede the system dialog with a plain
explanation. Note the project already applies exactly this standard to exact alarms —
`AI/design-decisions.md` requires "a plain reason shown first (§17)" — but not to
`POST_NOTIFICATIONS`, which is the permission that actually gates everything.

---

### H3 — On device boot, the full app startup runs and races the boot receiver, and can swallow every missed dose

This is the most consequential finding, and it is a genuine race, not a theoretical one.

**Mechanism.** `BootReceiver` fires → `ReminderRecovery.Run(this, resendMissed: true)`
([ReminderRecovery.cs:16–50](Animal%20Diary%20App/Platforms/Android/ReminderRecovery.cs#L16-L50)) resolves services from
`IPlatformApplication.Current?.Services`. For that provider to exist, .NET MAUI's
`MauiApplication.OnCreate` must have run — and that method also executes
`Services.GetRequiredService<IApplication>()`, which constructs `App`. `App`'s constructor
ends with `_ = StartAsync()` ([App.xaml.cs:48](Animal%20Diary%20App/App.xaml.cs#L48)).

So a *headless, boot-triggered* process start also runs the entire launch path, including
[App.xaml.cs:196–208](Animal%20Diary%20App/App.xaml.cs#L196-L208):

```csharp
await _reminderScheduler.CatchUpAndRefreshAsync(resendMissed: false);
```

Both calls contend for the same `_gate`. **If the launch path wins**, it walks every
past-due `Pending` instance with `resendMissed: false`, marks them all `Fired`
([:325](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L325)), and the boot
receiver's `resendMissed: true` pass then finds `pendingPast` empty and re-sends nothing.

**User impact.** The phone dies overnight or reboots for an update. Doses that fell in the
off window are exactly what the boot receiver exists to recover — and roughly half the
time (whichever task grabs the semaphore first), they are silently converted to "already
delivered" and never surface. The carer sees nothing.

**Secondary damage from the same mechanism.** Every reboot also runs, in a background
process with no window: the cloud launch sync, the billing/entitlement init, and
`_analytics.Track(AnalyticsEvents.AppOpened)` — so `app_opened` counts reboots as
launches, quietly corrupting the retention metrics described in `AI/analytics.md`.

**Confidence.** High on the code paths (all verified in this repo); the MAUI
`OnCreate` → `GetRequiredService<IApplication>()` step should be confirmed on a device
with a log line in `App`'s constructor, since it is framework behaviour rather than
project code. The fix is worth making either way: the two entry points should not both
be able to run a catch-up with contradictory `resendMissed` values.

---

## 4. High findings

### H4 — `ClearPendingAsync` deletes past-due instances and dismisses notifications already on screen

[MedicationReminderScheduler.cs:255–263](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L255-L263):

```csharp
var pending = (await _instances.GetByMedicationAsync(medicationId))
    .Where(i => i.Status == ReminderStatus.Pending)   // no time filter
    .ToList();

await _notifications.CancelNotifications(pending.Select(i => i.NotificationId));
await _instances.DeleteAllAsync(pending);
```

Two separate defects:

**(a) It erases missed-dose evidence.** The filter is status-only, so it includes
instances whose time has *passed* but which no catch-up has resolved yet. Any caller of
`SyncMedicationAsync` running before the catch-up deletes them permanently. This is not
hypothetical: [App.xaml.cs:196](Animal%20Diary%20App/App.xaml.cs#L196) and
[App.xaml.cs:212](Animal%20Diary%20App/App.xaml.cs#L212) start the reminder catch-up and the cloud
launch sync as two **parallel** `Task.Run` blocks, and the cloud sync calls
`SyncMedicationAsync` per affected medication
([CloudSyncService.cs:251](Animal%20Diary%20App/Data/Services/Cloud/CloudSyncService.cs#L251)). The
gate serializes them but does not order them. Cloud-sync-first ⇒ the doses the boot
receiver was about to re-send are gone.

**(b) Cancelling dismisses delivered notifications.** The plugin's `Cancel(id)` calls
`AlarmManager.Cancel(...)` **and** `NotificationManager.Cancel(id)` — the latter removes
an already-posted notification from the shade. So editing a medication, resuming a paused
pet, or a background cloud sync silently wipes a reminder sitting unacted in the carer's
notification tray. For `MarkDoseHandledAsync` that behaviour is correct and desirable;
for a bulk re-materialization it is data loss the user experiences as "the reminder
vanished".

---

### H5 — Missed-dose re-sends are silently dropped precisely on the slow boots they exist for

[MedicationReminderScheduler.cs:294](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L294) captures
`now` once. The `pendingPast` loop then performs **one sequential `await` SQLite UPDATE
per past instance** ([:306–327](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L306-L327)), plus a
`GetStatusAsync` query each. Only afterwards does
[:400](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L400) schedule the re-send:

```csharp
NotifyTime = now.AddSeconds(2),     // deliver right away
```

The plugin's `AndroidScheduleOptions.AllowedDelay` defaults to **1 minute**, and
`ShowLater` begins:

```csharp
if (request.Schedule.Android.IsValidNotifyTime(DateTime.Now, request.Schedule.NotifyTime) == false)
{
    LocalNotificationCenter.Log("NotifyTime is earlier than (DateTime.Now - Allowed Delay), notification ignored");
    return false;
}
```

So if more than ~62 seconds of wall clock elapse between capturing `now` and reaching
`Show()`, the re-send is **discarded with only a plugin log line** — and per H1 the app
never learns.

**User impact.** A cold boot with a large backlog (many instances, cold disk, a device
still starting dozens of other apps) is the exact scenario that is both (i) slowest and
(ii) most likely to have real missed doses. The mechanism fails hardest under the load it
was designed for.

**Fix shape:** compute `NotifyTime` from a fresh `DateTime.Now` at the moment of sending
(or schedule with no `NotifyTime` so the plugin takes its immediate `ShowNow` path).

---

### H6 — Boot recovery does far more work than a broadcast receiver's ~10 s budget allows

[ReminderRecovery.cs:19](Animal%20Diary%20App/Platforms/Android/ReminderRecovery.cs#L19) uses `GoAsync()`. That keeps the
receiver alive off the main thread but **does not extend the ANR window** — the system
still expects completion in roughly 10 seconds (WorkManager's own implementation budgets
8 s for exactly this reason).

The work actually performed inside that window:

1. `AppDatabase.EnsureInitializedAsync()` (table creation on first run after an update),
2. N sequential SQLite UPDATEs + a `GetStatusAsync` per past instance,
3. the missed-dose re-send loop,
4. **a full cancel-and-rebuild of every medication's alarms** — up to 400 cancels and 400
   schedules, each of which does a read-modify-write of the plugin's entire
   SharedPreferences pending list (see M3),
5. `ReconcileMissedAsync` — a 14-day sweep across every medication and schedule,
6. `PruneHistoryAsync`,
7. `DailyCareReminderScheduler.RefreshAsync()` — a full pending-items computation per pet.

**User impact.** On a real device with several pets and medications this will routinely
overrun. When the process is killed mid-pass, the medications not yet reached are left
with **no armed alarms at all** — `ClearPendingAsync` has already cancelled them — and
nothing retries until the user next opens the app. Boot recovery, the app's headline
reliability feature, is the piece most likely to be killed halfway.

**Best practice.** Android's guidance for work of this size started from a broadcast is
to enqueue a `WorkManager` job (or `JobScheduler` task) and return immediately;
WorkManager also re-registers itself across reboots.

---

### H7 — iOS: the 400-instance budget is more than six times the OS cap of 64

`GlobalPendingBudget = 400` and `MaxInstancesPerMedication = 70`
([MedicationReminderScheduler.cs:30–40](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L30-L40))
are sized against Android's ~500 alarm limit, and `AI/known-constraints.md` documents
that reasoning. iOS caps an app at **64 pending `UNNotificationRequest`s**; beyond that
the system keeps only the soonest-firing and discards the rest. The plugin's iOS
implementation calls `AddNotificationRequestAsync` with no cap handling of its own.

The project builds `net9.0-ios` today. Compounding factors on that target:

- There is no iOS equivalent of `BootReceiver`/`TimeChangeReceiver`, so re-arming happens
  **only** on app launch (already noted in `AI/known-constraints.md`).
- Combined with the 64-cap, a household with two pets on several medications will have its
  horizon truncated to a couple of days while the app believes it has fourteen.
- Per H1, the truncation is invisible: instances are still written `Pending` and later
  stamped `Fired`.

**Treat as a release blocker for iOS**, not for the current Android release. The budget
needs to be platform-conditional (≤ 60 on iOS, leaving headroom for the daily-care and
missed-dose ids), and the horizon shortened to match.

**Reference:** the 64-request limit is long-standing `UNUserNotificationCenter` behaviour;
see [Apple Developer Forums — *Does UNNotificationRequest have a 64-notification scheduling limit?*](https://developer.apple.com/forums/thread/811171)
and [*UserNotifications local notifications limits*](https://developer.apple.com/forums/thread/682509).

---

### H8 — `LastSeen` records the last *catch-up*, not the last time the app was alive

[MedicationReminderScheduler.cs:44](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L44) describes the
marker as "the last time the app was confirmed running", but `SetLastSeen` is called from
exactly one place — the end of `CatchUpAndRefreshCoreAsync`
([:349](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L349)). It is never
refreshed on `OnSleep`, `OnResume`, or during a session.

**User impact.** A carer opens the app Monday 08:00. They use it all week without a
reboot (`OnResume` does not stamp the marker). Thursday the phone's battery dies and they
reboot Friday 07:00. The boot catch-up computes the "device-off window" as **four days**,
so every unlogged dose in those four days satisfies `inst.ScheduledTime > lastSeen` and is
re-sent as missed — even though every one of those notifications was delivered normally
and the carer simply gave the pills without tapping.

The result is a burst of "missed dose" notifications for doses that were never missed —
the precise kind of false alarm `AI/app-voice.md` §8 forbids ("a reminder is not an
emergency"). `AI/known-constraints.md` describes this as "in rare timing windows a
reminder may be re-sent once"; the real window is "everything since the last cold start",
which for a habitually-backgrounded app is days.

**Fix shape:** stamp `LastSeen` on `OnSleep`/`OnResume` (and optionally on a timer), so
the off-window is genuinely the off-window.

---

### H9 — The reconciler writes clinical "Missed" facts the owner never logged, and the vet report prints them

[MedicationDoseReconciler.cs:69](Animal%20Diary%20App/Data/Services/Notifications/MedicationDoseReconciler.cs#L69) writes a
durable row for every scheduled dose in the last 14 days with no log:

```csharp
await _doseLogService.SetStatusAsync(med.Id, med.PetId, occurrence.Date, occurrence.TimeOfDay, DoseStatus.Missed);
```

Those rows are indistinguishable from owner-logged ones — there is no provenance column.
They flow straight into the PDF handed to a vet:
[VetReportDataBuilder.cs:179](Animal%20Diary%20App/Data/Services/Reports/VetReportDataBuilder.cs#L179)
counts `DoseStatus.Missed`, and
[MedicationsSection.cs:72](Animal%20Diary%20App/Data/Services/Reports/Document/Sections/MedicationsSection.cs#L72)
renders `"{med.MissedCount} missed"`.

But "no log row" means **"the owner didn't tap"**, not "the dose wasn't given".

**User impact.** An owner who reliably medicates but logs sporadically hands their vet a
report asserting dozens of missed doses. A vet reading non-adherence may change a dose,
switch a drug, or investigate a treatment failure that isn't there. This is the highest-
stakes output the app produces.

**This violates the project's own non-negotiable rules**, stated in `AI/README.md` and
`AI/domain.md`: *"The vet report only states owner-logged facts — no interpretation,
severity, or advice"*, and *"Felova records; it never judges."* An inferred miss is
interpretation, and H8 makes it systematically over-inclusive.

**Fix shape (product decision, not just code):** either add provenance to
`MedicationDoseLog` and exclude inferred rows from report counts, or report them under a
distinct, honest label ("not logged" rather than "missed"). The Journal chip behaviour
that depends on a row existing ("a dose is *given* once any log row exists") can keep
working off the same rows.

---

## 5. Medium findings

### M1 — One notification channel, named "General", at `IMPORTANCE_DEFAULT`

The app never creates a channel, so the plugin lazily creates its fallback on first use:
id `Plugin.LocalNotification.GENERAL`, name **"General"**, `Importance = Default`.

Four consequences:

1. **No heads-up display.** `IMPORTANCE_DEFAULT` makes a sound but never peeks over the
   current screen; `IMPORTANCE_HIGH` is what reminder-class notifications normally use. A
   medication reminder arriving while the carer is using the phone is a silent tray entry.
2. **Untranslated, unbranded.** The only notification control the user has is a row
   labelled "General" in system settings — in English, regardless of app language.
3. **No per-category control.** Medication reminders, missed-dose catch-ups, and the
   *silent* daily-care nudge all share one channel, so a carer who wants to mute the daily
   nudge must mute medication reminders too. §8.7's intent (only medication reminders make
   a sound) is implemented at the request level via `SetSilent`, which the user cannot
   re-tune. Android's channel guidance is explicitly one channel per category.
4. **This gets harder to fix after release.** Channel importance is immutable once
   created; changing it later requires *new channel IDs*, and existing installs keep the
   old channel with its old settings. Cheap now, permanent debt after launch.

### M2 — `CategoryType` is never set

The plugin calls `builder.SetCategory(request.CategoryType.ToNative())` with whatever
default the request carries; the app sets nothing. Marking reminders `CATEGORY_REMINDER`
(or `CATEGORY_ALARM`) is what lets a user's Do Not Disturb rules pass medication
reminders through while blocking everything else. As built, a carer who runs DND overnight
receives nothing and has no way to whitelist just the doses.

### M3 — Every launch tears down and rebuilds all ~400 alarms, and the plugin's store is O(n²)

`CatchUpAndRefreshCoreAsync`
([:331–340](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L331-L340)) calls
`SyncMedicationCoreAsync` for **every** medication unconditionally — cancel all, delete
all, re-expand, re-insert, re-arm — even when nothing changed.

The plugin makes that far more expensive than it looks. Each `Show()` ends with
`NotificationRepository.AddPendingRequest(request)`, which **deserializes the entire
pending list from SharedPreferences, filters it, appends one entry, and re-serializes the
whole thing**. At the 400 budget, one launch performs ~400 cancels and ~400 schedules,
each rewriting a JSON list averaging ~200 entries — tens of thousands of request
(de)serializations and ~800 SharedPreferences commits, plus ~400 individual SQLite
round trips on the app's side (`InsertAllAsync` is transactional, but the status-update
loop is not).

**User impact:** measurably slower and more battery-hungry startup that degrades as the
user adds pets and medications, on every single app open. It also widens the H6 window in
which alarms are momentarily un-armed.

**Fix shape:** diff desired vs. armed instead of teardown-and-rebuild. Most launches
change nothing, so most launches should issue zero plugin calls.

### M4 — No `MY_PACKAGE_REPLACED` receiver

`AndroidManifest.xml` declares `RECEIVE_BOOT_COMPLETED` and the app handles
`BOOT_COMPLETED`, `TIME_SET`, `TIMEZONE_CHANGED`, and `DATE_CHANGED` — but nothing handles
the app being updated. Play Store auto-updates happen in the background, without the user
opening the app afterwards. If pending alarms do not survive the package replacement
(widely reported; the alarms doc is explicit only about reboot), every reminder is dead
until the carer next opens Felova — which for a well-behaved reminder app might be days.

**Verify on device** (install v1, arm a reminder, install v2 over it, check with
`adb shell dumpsys alarm`), then add `ACTION_MY_PACKAGE_REPLACED` to the existing
`ReminderRecovery` path if confirmed. Low effort, and the receiver already exists.

### M5 — Time-zone travel marks un-fired reminders `Fired` and manufactures missed doses

`TimeChangeReceiver` routes to `CatchUpAndRefreshAsync(resendMissed: false)`. Fly east by
eight hours: the local wall clock jumps forward, so today's not-yet-fired occurrences are
now "past". They are stamped `Fired` although they never fired, and the reconciler then
stamps the corresponding dose logs `Missed` (feeding H9). The carer gets no reminder and a
false record.

**DST itself is handled correctly** and deserves credit: `MedicationScheduleExpander` does
pure wall-clock arithmetic, and the plugin converts via `DateTime.ToUniversalTime()`, which
applies the adjustment rule in effect *at the future instant* — so "09:00 every Monday"
survives a DST boundary. Two residual edges are unspecified rather than wrong: an
occurrence at a **nonexistent** local time (spring-forward gap) and an **ambiguous** one
(fall-back repeat) resolve by .NET's standard-time default rather than any deliberate
policy. Also note `ACTION_DATE_CHANGED` fires at midnight, i.e. *before* the ~02:00–03:00
DST transition, so the midnight re-arm does not cover it; whether `ACTION_TIME_CHANGED` is
broadcast on DST transitions is device-dependent, so it should not be the sole guard.

### M6 — Delivery drift under Doze is larger than the docs claim

The design decision is sound and I verified it against the plugin source:
`ShowLater` checks `CanScheduleExactAlarms()` and falls back to `SetAndAllowWhileIdle`
when the capability is absent — exactly as `AI/design-decisions.md` asserts. No change of
approach is recommended.

But the accepted trade-off is understated. `AI/known-constraints.md` says delivery "can
drift (minutes, more under deep Doze)". Per Android's
[alarms documentation](https://developer.android.com/develop/background-work/services/alarms/schedule):
for apps targeting Android 12+, the system invokes an inexact alarm **within one hour** of
the trigger time, and may delay a time-windowed alarm by **at least 10 minutes**. Under
Doze, allow-while-idle alarms fire **at most once per nine minutes per app**.

**User impact:** two medications due at 08:00 and 08:05 can arrive nine or more minutes
apart; an 08:00 reminder can legitimately land at 08:45. That is defensible for this
product — but it should be stated accurately in the constraints doc, and it interacts with
H5 (a batch of re-sends issued at once can be throttled behind the nine-minute rule).
App Standby buckets compound it: an app opened rarely — which Felova is, by design — can
fall into the `rare`/`restricted` bucket where allow-while-idle quotas tighten further.

### M7 — `NotificationContent.Recurrence` defaults to `Daily`

[INotificationService.cs:22](Animal%20Diary%20App/Data/Services/Data/Device/INotificationService.cs#L22):

```csharp
public NotificationRecurrence Recurrence { get; set; } = NotificationRecurrence.Daily;
```

Every current call site passes `Once` explicitly, so nothing is broken today. But the
default on the DTO is the one thing the architecture's **first non-negotiable rule**
forbids — "never rely on infinite OS notification recurrence". A future contributor who
omits one line gets an infinite OS recurrence silently. The default should be `Once`.

### M8 — Boot-armed notification copy can be in the wrong language

`NotificationMessages` reads `LocalizationManager.Instance`, whose language is applied in
`App.StartAsync` ([App.xaml.cs:148–162](Animal%20Diary%20App/App.xaml.cs#L148-L162)) from the saved
setting. `ReminderRecovery` never applies it and races that path (H3).
`AI/known-constraints.md` calls this "a minor edge case", but the consequence is bigger
than the label: reminders armed during boot recovery cover the **entire 14-day horizon**,
so a German user who reboots can get English reminders until the next time they open the
app and trigger a re-arm.

---

## 6. Low findings / code quality

- **L1 — Dead scaffolding.** `AppNotificationType` has no references anywhere. The
  `MoodCheckIn`, `WeightCheckIn`, and `Appointment` helpers in `NotificationIds` and
  `NotificationMessages` are unused, yet their resx strings must be maintained in both
  `AppStrings.resx` and `AppStrings.de.resx` for nothing.
- **L2 — Legacy cancellation runs forever.** `CancelMedicationCoreAsync`
  ([:251](Animal%20Diary%20App/Data/Services/Notifications/MedicationReminderScheduler.cs#L251)) still cancels
  `NotificationIds.AllMedicationReminders(medicationId)` — 10 ids from a scheme no longer
  used. Each is a plugin `Cancel` doing two SharedPreferences read-modify-writes, on
  every cancel, forever. It can be gated behind a one-time migration flag.
- **L3 — No error handling in `MedicationReminderScheduler`.** There is no `try/catch`
  anywhere in the class. One SQLite or plugin exception mid-loop aborts the whole
  catch-up, leaving the remaining medications un-armed; the only surfacing is a
  `Debug.WriteLine` in the caller. `DailyCareReminderScheduler` *does* wrap its core
  ([:62–66](Animal%20Diary%20App/Data/Services/Notifications/DailyCareReminderScheduler.cs#L62-L66)) — the
  inconsistency is the bug.
- **L4 — No tests exist in the repo.** `MedicationScheduleExpander` and `PendingEngine`
  are pure functions and the highest-value, lowest-cost tests in the app. `Expand`'s
  boundary contract (`candidate <= from` ⇒ +7 days) is exactly the kind of thing that
  should be pinned before anyone touches it.
- **L5 — `INotificationService.RequestNotificationPermission()`** (the non-generic
  overload) has an implementation and zero callers.
- **L6 — Filename/type mismatch:** `NotificationsService.cs` defines
  `class NotificationService`.
- **L7 — `PruneHistoryAsync` operates on a stale in-memory list** fetched before the
  re-arm loop inserted new rows. The comment acknowledges and justifies it; it is correct
  today but fragile to reordering.

---

## 7. Verified correct (checked, no action needed)

Worth recording so these aren't re-litigated:

- **The exact-alarm decision holds.** Verified against plugin source: with
  `SCHEDULE_EXACT_ALARM` absent, `CanScheduleExactAlarms()` returns false on API 31+ and
  `ShowLater` uses `SetAndAllowWhileIdle`. No `SecurityException` risk, no Play policy
  exposure. The manifest comment is accurate.
- **Notification IDs are deterministic and collision-free** across all five ranges at any
  realistic entity count. `ForInstance` derives from an AUTOINCREMENT id that is pruned,
  never renumbered.
- **Soft-delete filtering is correct throughout.** `GetMedicationByIdAsync`,
  `GetAllMedicationsAsync`, `GetMedicationsByPetIdAsync`,
  `GetMedicationSchedulesByMedicationIdAsync`, `GetPetsAsync`, and `GetPetByIdAsync` all
  filter `IsDeleted == false`, so a pet or medication deleted on another device stops
  producing reminders here once the tombstone syncs.
- **Cloud sync re-arms correctly.** `AffectedMedications` is populated from the medication,
  schedule, and dose-log table maps, and each is routed through the idempotent
  `SyncMedicationAsync`.
- **The undo/re-arm contract is honoured at every call site.** Every
  `ClearStatusAsync` in `JournalLogViewModel` is followed by `SyncMedicationAsync`, and
  every outcome write by `MarkDoseHandledAsync` — including inside undo closures. This is
  the trap `AI/domain.md` warns about and the code gets it right.
- **The `_gate` semaphore is applied consistently** — every public mutation takes it,
  every `*Core` method assumes it, and no `*Core` method is reachable without it.
- **`InsertAllAsync` is genuinely atomic**, so process death mid-materialization cannot
  leave a half-armed medication (only a wholly un-armed one — see H6).
- **Pause semantics are correct and complete.** `SyncMedicationCoreAsync`,
  `ResendMissedAsync`, and `DailyCareReminderScheduler.RefreshCoreAsync` all check
  `IsPaused`, and pause sets the flag *before* cancelling so a concurrent catch-up can't
  race it back on.
- **`AppResetService` cancels all OS notifications before wiping**, and both
  `MedicationReminderScheduler.ClearPersistedState` and
  `DailyCareReminderSettings.ClearPersistedState` are wired to the reset.
- **DateTime equality in `MarkDoseHandledAsync` is safe** — sqlite-net stores `DateTime`
  as ticks by default, so `ScheduledTime.TimeOfDay == time` compares exactly.
- **The daily-care reminder's "today only" design is right**, and
  `DailyCareReminderSettings.Time` validates its stored ticks against a 24-hour range.
- **Notification copy is fully localized and on-voice**, with `SafePet`/`SafeMed`
  fallbacks for empty names.

---

## 8. Recommended remediation, in order

Effort is rough dev-days. "Ship-blocking" means for the current Android release.

| Order | Fix | Addresses | Effort | Blocking? |
|---|---|---|---|---|
| 1 | Make `ScheduleNotification` return `Task<bool>`; persist an `Armed` flag or don't write the instance on failure; log/surface failures | H1 | 0.5 d | **Yes** |
| 2 | Add a notifications-health check: request permission at first pet/first reminder opt-in with a plain reason; re-check `areNotificationsEnabled()` on resume; show an in-app banner + settings deep-link when off | H1, H2 | 1–2 d | **Yes** |
| 3 | Give the boot receiver sole ownership of boot catch-up — have `App.StartAsync` skip its catch-up when no window exists, or pass a single reconciled `resendMissed` decision | H3 | 0.5 d | **Yes** |
| 4 | Time-filter `ClearPendingAsync` to `ScheduledTime > now`, and resolve past-due instances before any re-materialization | H4a | 0.5 d | **Yes** |
| 5 | Recompute `NotifyTime` from a fresh `DateTime.Now` at send time in `ResendMissedAsync` | H5 | 0.25 d | **Yes** |
| 6 | Stamp `LastSeen` on `OnSleep`/`OnResume` | H8 | 0.25 d | **Yes** |
| 7 | Declare explicit channels — `medication` (`IMPORTANCE_HIGH`, `CATEGORY_REMINDER`), `daily_care` (`IMPORTANCE_LOW`) — localized names/descriptions | M1, M2 | 0.5 d | **Yes** (immutable after release) |
| 8 | Decide the provenance question for inferred `Missed` rows and stop the vet report asserting un-logged doses as missed | H9 | 1–2 d | **Yes** (product call) |
| 9 | Move boot recovery to `WorkManager`; keep the receiver as a thin enqueue | H6, and hardens M4 | 1–2 d | No, next release |
| 10 | Diff desired vs. armed instead of teardown-and-rebuild | M3, M4b | 1–2 d | No |
| 11 | Add `ACTION_MY_PACKAGE_REPLACED` to `ReminderRecovery` (after device verification) | M4 | 0.25 d | No |
| 12 | Platform-conditional budget/horizon (≤ 60 pending on iOS) | H7 | 0.5 d | **Yes, before iOS** |
| 13 | Default `Recurrence` to `Once`; apply saved language in `ReminderRecovery` | M7, M8 | 0.25 d | No |
| 14 | Unit-test `MedicationScheduleExpander` + `PendingEngine`; delete dead scaffolding; gate legacy cancellation | L1–L6 | 1 d | No |

Items 1–8 are roughly **4–6 days** and take the subsystem from "architecturally right but
silently fallible" to production-ready on Android.

---

## 9. Needs on-device verification

Three claims rest on framework/OEM behaviour rather than this repo's code, and should be
confirmed before acting:

1. **H3** — that `MauiApplication.OnCreate` constructs `App` (and therefore runs
   `StartAsync`) during a boot-receiver-only process start. Add a log line in `App`'s
   constructor and reboot. *(The fix is worth making regardless of the outcome.)*
2. **M4** — whether pending `AlarmManager` alarms survive a Play-style package replace.
   Verify with `adb shell dumpsys alarm | grep felova` before and after an over-install.
3. **M6** — real-world drift and standby-bucket behaviour on the target OEMs
   (Xiaomi/Samsung are already flagged in `AI/known-constraints.md`). Worth one
   multi-day soak on a physical device with battery optimization *on*, which is the
   default state for most users.

---

## 10. Sources

- [Android — Schedule alarms](https://developer.android.com/develop/background-work/services/alarms/schedule)
- [Android — Notification runtime permission](https://developer.android.com/develop/ui/views/notifications/notification-permission)
- [Android — Schedule exact alarms are denied by default (Android 14)](https://developer.android.com/about/versions/14/changes/schedule-exact-alarms)
- [Android — Optimize for Doze and App Standby](https://developer.android.com/training/monitoring-device-state/doze-standby)
- [Plugin.LocalNotification — Android `NotificationServiceImpl` (v12.0.1)](https://github.com/thudugala/Plugin.LocalNotification/blob/v12.0.1/Source/Plugin.LocalNotification/Platforms/Android/NotificationServiceImpl.cs)
- [Plugin.LocalNotification — `NotificationRepository`, `AndroidScheduleOptions`, `NotificationChannelRequest` (v12.0.1)](https://github.com/thudugala/Plugin.LocalNotification/tree/v12.0.1/Source/Plugin.LocalNotification)
- [Plugin.LocalNotification issue #465 — exact alarms without permission check](https://github.com/thudugala/Plugin.LocalNotification/issues/465)
- [Apple Developer Forums — 64-notification scheduling limit](https://developer.apple.com/forums/thread/811171)
- [Apple Developer Forums — UserNotifications local notification limits](https://developer.apple.com/forums/thread/682509)
- [CommonsWare — ACTION_BOOT_COMPLETED and the receiver time budget](https://commonsware.com/blog/2017/06/12/action_boot_completed-intentservice-android-8p0.html)
- Project docs: `AI/design-decisions.md`, `AI/known-constraints.md`, `AI/domain.md`, `AI/architecture.md`, `AI/app-voice.md`
