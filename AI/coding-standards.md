# Coding Standards & Conventions

> **For AI assistants: follow these so new code reads like the code around it.**
> Prefer consistency over introducing new patterns; when adding, mirror the
> nearest existing example. Update this file when a convention actually changes —
> not for one-off choices.

---

## General

- Namespace `Animal_Diary_App`; folders mirror namespaces.
- Match the repo's **high comment density** — comments explain *why*, not *what*.
- **Smallest-change ethos:** extend existing models/services over adding new
  ones; a new feature copies the closest existing example.
- Register every new service/VM in `MauiProgram`; expose new child VMs on
  `MainViewModel`.

## MVVM

- VMs derive from `BaseViewModel` (`SetProperty` / `INotifyPropertyChanged`).
- Async loads run in `OnAppearing()`, not constructors. List state is
  `ObservableCollection<T>`.
- Commands are `ICommand` via `Command` / `Command<T>`. List-item commands reach
  the page VM with
  `{Binding Source={RelativeSource AncestorType={x:Type ContentPage}}, Path=BindingContext.…}`.
- **Validation:** VMs expose `XxxError` strings + a composite `CanSave…` bool;
  error labels bind visibility through `StringToBoolConverter`.
- Draft-holding VMs implement `IResettableDraft` and are added to
  `MainViewModel._draftViewModels` so a data reset clears them.

## Input sheets — the uniform contract

Every input type is a thin `ContentView` wrapping the shared `FelovaBottomSheet`
plus one small `*SheetViewModel`. To add one, copy `SeizureSheetView(Model)` or
`AppetiteSheetView(Model)` (cleanest examples) and wire all touch-points:

1. VM: `OpenAsync(petId, petName, date)`; `IsPresented`/`Title`/`Subtitle`/
   `SaveCommand`/`DismissCommand`; and
   `event Action<JournalSaveResult>? Saved` — every save returns a
   `JournalSaveResult(Message, UndoAsync)` (warm line **plus** the undo that
   reverses it).
2. Register the VM in `MauiProgram`; expose it on `MainViewModel`.
3. Declare the `<views:XSheetView … BindingContext="{Binding XSheetVM}"/>`
   overlay in `CalendarPage.xaml`.
4. Subscribe/unsubscribe `Saved` in `CalendarPage.xaml.cs`; handle it in
   `OpenSheetForKindAsync`.
5. If it belongs under "+", add it in `JournalLogViewModel.BuildAddOptionsAsync`
   (gated on the pet's care plan).

**Overlay hosting landmine:** a sheet overlay is a `ContentView` placed with
`Grid.RowSpan`. Bind the **wrapper's `InputTransparent`** to the *inverse* of its
presented flag (`InvertedBoolConverter`) so a closed sheet doesn't eat touches
but an open one captures them. Do **not** use `CascadeInputTransparent` (a
`Layout`-only property) — on a `ContentView`, `InputTransparent="True"` makes the
whole subtree non-interactive and the open sheet becomes visual-only.

**One-per-day vs event:** Mood/Weight/Appetite sheets *replace* the day's row
(undo restores the previous); Glucose/Seizure *insert* a new row. (See
[domain.md](domain.md).)

Animations live in the **page** (`CalendarPage.xaml.cs`), not the VM. Presentation
hints on `TimelineItem`/`DoseItem` (tint, rotation) are resolved from
`Application.Current.Resources` — an accepted convention, flagged as soft debt in
[known-constraints.md](known-constraints.md).

## SQLite & migrations

- `[PrimaryKey, AutoIncrement] int Id`; `[Indexed]` foreign keys where queried.
- Persisted enums: `[StoreAsText]` on the **enum type**, not the property.
- **Additive** property changes need no migration (columns auto-add; nothing is
  dropped — keep old columns for back-compat). A new **table** must be added to
  `AppDatabase.InitAsync`.
- Reuse repositories in `Services/Data`/`Services/Journal`; don't open the
  connection directly from a VM.

## Localization (EN + DE, live switch)

Every user-facing string is localized in **both** `Resources/Strings/
AppStrings.resx` (English/neutral) and `AppStrings.de.resx` (German). Keys are
screen-prefixed (`Common_*`, `Nav_*`, `Main_*`, `Med_*`, `Journal_*`,
`Appetite_*`, `Condition_*`, `Export_*`, `Notif_*`, `Validation_*`, …).

- **Engine:** `Helpers/LocalizationManager.Instance` over a `ResourceManager`.
  `SetLanguage("en"|"de")` swaps the culture and raises a **null-named
  `PropertyChanged`**, so every binding re-reads → the whole UI re-translates
  live, no restart.
- **XAML:** `xmlns:loc="clr-namespace:Animal_Diary_App.Helpers"` then
  `Text="{loc:Translate Key}"` (works on Label/Button/Entry `Text`/`Placeholder`
  and `<Span Text=…>`).
- **C#:** `LocalizationManager.Instance.GetString("Key")` / `.Format("Key", args)`.
- **Persistence:** `SettingsService.Get/SetLanguageAsync()` (`Language` key in
  `AppSettings`); a null result means "not chosen yet" and gates the first-launch
  picker.
- **Gotchas that force a specific pattern:**
  - `Setter.Value` can't bind → `{loc:Translate}` doesn't work in a `<Setter>`.
    Move placeholders/empty-states to VM display properties driven by a `bool`.
  - `StringFormat` with translatable words → use a localized VM/computed property
    (e.g. `SelectedDateDisplay`, `MedicationsHeader`, `Pet.AgeDisplay`).
  - **Never translate stored data.** Pet types and moods are canonical English
    keys localized only for display (`PetTypeNames.Localize`,
    `MoodLevelExtensions.GetDisplayName`); pet/med names and custom types are
    shown verbatim.
  - Notifications pull localized templates (`Notif_*`, `{0}`/`{1}` = pet/med).
  - Intentionally untranslated: the brand name (Felova) and the language names on
    the picker.

## Colours & spacing

- Tokens live in `Resources/Styles/Colors.xaml`. **Prefer the Rockpool token
  generation** the Journal uses (`Glass`, `*Tint`, `Teal*`, `PaperWarm`, `Washi`,
  `NoteInk`, `Rose*`). An older hex-literal palette still coexists — see
  [known-constraints.md](known-constraints.md); don't add new usages of it.
- Spacing: 16 padding, 18 section spacing, 12–14 control gaps, rounded corners
  (14–28).
- App-wide converters are XAML resources in `App.xaml`
  (`StringToBoolConverter`, `BoolToOpacityConverter`, `InvertedBoolConverter`).

## Building / verifying

- **Fastest local compile check** (on Windows):
  `dotnet build "Animal Diary App/Animal Diary App.csproj" -f net9.0-windows10.0.19041.0 -c Debug -clp:ErrorsOnly`
  Expect ~0 errors and a large **pre-existing** warning count — don't chase it.
- Before deleting a VM member, confirm it has no live XAML binding, e.g.
  `grep -rhoE 'CalendarVM\.[A-Za-z0-9_]+' "Animal Diary App/Data/View/"*.xaml | sort -u`
  (repeat per VM). Unbound **and** unused by other members = safe to remove.
