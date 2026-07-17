# Architecture

> **For AI assistants: this file is the structural map.** It says where things
> live and how a request flows through the app. For the *rules* those pieces
> obey read [domain.md](domain.md); for *why* the shapes are what they are read
> [design-decisions.md](design-decisions.md). Update this file only when the
> folder layout, the subsystem boundaries, DI wiring, navigation, or startup
> change.

---

## Pattern: MVVM + constructor DI

UI (XAML pages/views) binds to ViewModels; ViewModels call Services; Services own
the SQLite connection and the device APIs. Nothing above a service touches SQLite
or the notification plugin directly.

Everything is registered in **`MauiProgram.CreateMauiApp`** as a singleton
(pages are `Transient`) and resolved by constructor injection.

## Folder structure

```
Animal Diary App/
├── App.xaml(.cs)             App resources + startup flow (see below)
├── AppShell.xaml(.cs)        Tabbed Shell host (Today / Journal / Pets)
├── MauiProgram.cs            DI registration + .UseLocalNotification() + Syncfusion core
├── Data/
│   ├── Models/               SQLite entities + catalogs (Pet, Medication, CarePlan, Condition…)
│   ├── Services/
│   │   ├── Data/             Repositories over SQLite (Pet/Medication/PetEntry/Settings/…)
│   │   │   └── Device/       Platform device boundary (INotificationService → plugin)
│   │   ├── Journal/          Care-plan, pending engine, typed entry stores (glucose/appetite/seizure)
│   │   ├── Notifications/    Reminder scheduling engine + device-agnostic messages/ids
│   │   └── Reports/          Vet PDF: 3 layers (data → document/sections → service)
│   ├── ViewModels/           One VM per screen/feature; all derive from BaseViewModel
│   └── View/                 XAML pages + reusable ContentViews (incl. the input sheets)
│       └── Controls/         Custom controls: FelovaBottomSheet, WeekCalendarView, charts…
├── Helpers/                  Converters, InputParser, MoodLevel, LocalizationManager, TranslateExtension
├── Platforms/                Per-platform entry points, Android receivers, manifest
└── Resources/                Styles (Colors/Styles), fonts, images, Strings (resx)
```

Namespace is `Animal_Diary_App`; folders mirror namespaces.

## ViewModel composition

- **`MainViewModel`** is a container resolved via DI. It exposes child VMs as
  properties: `CalendarVM`, `MainPageVM`, `PetVM`, `MedicationVM`, `SettingsVM`,
  `ConditionVM`, `JournalVM`, `ManageVM`, the per-type sheet VMs
  (`GlucoseSheetVM`, `MoodSheetVM`, `WeightSheetVM`, `AppetiteSheetVM`,
  `SeizureSheetVM`), the condition-setup sheet VMs (`DiabetesSetupVM`,
  `CkdSetupVM`, `EpilepsySetupVM`), and the vet-report surfaces
  (`ExportSheetVM` — the Pets-page export sheet; `ReportPreviewVM` — call its
  `Open(row)` before pushing `ReportPreviewPage`; `DocumentsVM` — the Documents
  page list).
- Pages set `BindingContext = mainViewModel` and bind through paths like
  `{Binding MedicationVM.SomeProperty}`.
- All VMs derive from **`BaseViewModel`** (`INotifyPropertyChanged` + `SetProperty`).
- Form VMs that hold draft state implement **`IResettableDraft`** so a global
  data reset can clear them (they're singletons; drafts would otherwise survive).
- **New feature work goes in new VMs/services, not by reshaping existing VMs.**
  (See [design-decisions.md](design-decisions.md).)

### Data loading

- Async loads run in **`OnAppearing()` overrides, not constructors.**
- List-bound state is `ObservableCollection<T>`.

## The four subsystems

Each is a coherent slice with its own folder. Details live in the linked docs.

1. **Data / SQLite** (`Services/Data/`) — one `AppDatabase` owns the async
   connection; repositories wrap tables. Schema and relationships → [domain.md](domain.md).
2. **Notifications** (`Services/Notifications/` + `Services/Data/Device/`) —
   turns medication schedules into concrete, bounded, reboot-surviving reminders.
   Design → [design-decisions.md](design-decisions.md).
3. **Journal** (`Services/Journal/` + the sheets) — care plan, the pure
   `PendingEngine`, typed entry stores, and the unified day timeline. Rules →
   [domain.md](domain.md).
4. **Reports** (`Services/Reports/`) — the vet PDF, layered DATA → DOCUMENT →
   SERVICE (QuestPDF), plus the report library around it: `ReportLibraryService`
   (the `Reports/` folder + `VetReportFile` rows), `ReportActions` (share /
   open-externally, shared by every surface), the Pets-page export sheet
   (`ExportSheetView(Model)`), `ReportPreviewPage` (pre-rendered page PNGs), and
   `DocumentsPage` (list / share / delete-with-undo). Rules → [domain.md](domain.md);
   layering + preview/deletion decisions → [design-decisions.md](design-decisions.md).

## Navigation

- **Shell tabs** — `AppShell` hosts three tab pages (`MainPage` = *Today*,
  `CalendarPage` = *Journal*, `PetsPage` = *Pets/Care*). The Shell's own chrome
  is hidden; a custom `BottomNavigation` control provides the tab bar. Pages are
  injected once and kept alive for the Shell's lifetime; switch tabs via
  `Shell` navigation, not by rebuilding pages.
- **Onboarding** runs as a plain `NavigationPage` stack (Language → Welcome →
  create pet → optional condition picker) *before* the Shell exists.
- After the first pet is saved, `App.SwitchToMainApp()` swaps the window root to
  a **freshly resolved** `AppShell` (so a post-reset relaunch never reuses stale
  page instances). Pushed pages like `ManagePetPage`, `CreatePetPage` (edit
  mode), `DocumentsPage`, and `ReportPreviewPage` are `ContentPage`s pushed onto
  the Shell/nav stack (ctor takes the shared `MainViewModel`).

## Startup flow (`App.StartAsync`)

1. Show a `LoadingPage`, then `AppDatabase.EnsureInitializedAsync()` (creates all
   tables once, idempotently).
2. Resolve the saved active pet (fallback: first pet).
3. Decide the landing page **lazily**, *after* applying language: no saved
   language → `LanguageSelectionPage`; else, no pets → onboarding `WelcomePage`;
   else → `AppShell`.
4. Off the UI path, run `CatchUpAndRefreshAsync(resendMissed: false)` to re-arm
   reminders (launch semantics — see [design-decisions.md](design-decisions.md)).

Android also wires **`BootReceiver`** (`BOOT_COMPLETED`) and
**`TimeChangeReceiver`** (`TIME_SET`/`TIMEZONE_CHANGED`/`DATE_CHANGED`), which
share `ReminderRecovery.Run`.
