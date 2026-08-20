namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Helpers;
using System.Windows.Input;
using System.Collections.ObjectModel;

public class MainPageViewModel : BaseViewModel
{
    public List<PetEntry> Entries { get; set; } = new();
    private readonly PetEntryService _petEntryService;
    private readonly PetService _petService;
    private readonly ActivePetService _activePetService;
    private readonly SettingsService _SettingsService;
    private readonly PendingItemsService _pendingItems;
    private readonly MedicationService _medicationService;
    private readonly MedicationDoseLogService _doseLogService;
    private readonly MedicationReminderScheduler _reminderScheduler;
    private readonly TodayCardService _todayCards;
    private readonly TodayCardSheetViewModel _cardPicker;
    private readonly CustomTrackerService _customTrackers;
    private readonly VetVisitService _vetVisits;

    public Pet ActivePet
    {
        get => _activePetService.ActivePet;
        set => _activePetService.ActivePet = value;
    }

    public MainPageViewModel(PetEntryService petEntryService, PetService petService, ActivePetService activePetService, SettingsService settingsService,
        PendingItemsService pendingItems, MedicationService medicationService, MedicationDoseLogService doseLogService, MedicationReminderScheduler reminderScheduler,
        TodayCardService todayCards, TodayCardSheetViewModel cardPicker,
        CustomTrackerService customTrackers, VetVisitService vetVisits)
    {
        _petEntryService = petEntryService;
        _petService = petService;
        _activePetService = activePetService;
        _SettingsService = settingsService;
        _pendingItems = pendingItems;
        _medicationService = medicationService;
        _doseLogService = doseLogService;
        _reminderScheduler = reminderScheduler;
        _todayCards = todayCards;
        _cardPicker = cardPicker;
        _customTrackers = customTrackers;
        _vetVisits = vetVisits;

        OpenAppointmentCommand = new Command(() => AppointmentRequested?.Invoke());

        // The two stat cards are stable instances refreshed in place — see TodayCardItem
        // for why they are not rebuilt per load.
        PrimaryCard = new TodayCardItem(TodayCardSlot.Primary, OnCardTapped);
        SecondaryCard = new TodayCardItem(TodayCardSlot.Secondary, OnCardTapped);

        _activePetService.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ActivePet))
            {
                OnPropertyChanged(nameof(ActivePet));
            }
        };

        // This VM is a singleton, so a greeting cached here would survive a live
        // language switch in the old language. Re-raise instead and let the getter
        // resolve the string fresh (the DaySelectionItem pattern).
        LocalizationManager.Instance.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(Greeting));
            OnPropertyChanged(nameof(CardHintText));
            // The cards resolve every string per read; they just need to be told.
            PrimaryCard.RefreshLocalized();
            SecondaryCard.RefreshLocalized();
        };
    }

    /// <summary>Time-of-day greeting on the Today header. Resolved per read — the
    /// hour can roll over while the page is alive, and the string must follow the
    /// active language. Refreshed on every appearance via <see cref="LoadTodayCareAsync"/>.</summary>
    public string Greeting => LocalizationManager.Instance.GetString(
        DateTime.Now.Hour switch
        {
            < 12 => "Main_GreetingMorning",
            < 18 => "Main_GreetingAfternoon",
            _ => "Main_GreetingEvening"
        });

    private decimal latestWeight;
    public decimal LatestWeight
    {
        get => latestWeight;
        set => SetProperty(ref latestWeight, value);
    }

    private PetEntry? EntryToday;

    public async Task LoadLatestWeightAsync()
    {
        if (ActivePet == null) return;

        EntryToday = await _petEntryService.GetLatestWeightEntryAsync(ActivePet.Id);
        // Clear on a pet with no weigh-ins too, or the previous pet's value lingers.
        LatestWeight = EntryToday?.Weight ?? 0;
    }

    // ── The two customizable stat cards ──────────────────────────────────────
    //
    // Today shows exactly two records and the owner picks which. Each card states the
    // pet's LAST value for its record and when it was written down — no streaks, no
    // scores, no "N days since" (see Data/Models/TodayCards.cs and AI/domain.md).
    //
    // The pair defaults from the pet's conditions and is remembered per pet the moment
    // the owner chooses; TodayCardService owns both halves.

    /// <summary>Which stat-card load is the current one — the pair is keyed to the
    /// active pet, so a slow load for the previous pet must not paint over this one.</summary>
    private int _cardGeneration;

    public TodayCardItem PrimaryCard { get; }
    public TodayCardItem SecondaryCard { get; }

    private bool _showCardHint;

    /// <summary>Set the moment a card is tapped, before the flag reaches the database.
    /// A reload whose flag read overlapped that tap would otherwise bring the retired
    /// hint back for the rest of the session.</summary>
    private bool _cardsDiscovered;

    /// <summary>True until the owner has opened the picker once. The spelled-out hint is
    /// how the cards announce they can be changed; after that the small pencil on each
    /// card is enough, and a permanent instruction would only make Today look like a
    /// settings screen.</summary>
    public bool ShowCardHint
    {
        get => _showCardHint;
        private set => SetProperty(ref _showCardHint, value);
    }

    /// <summary>"Tap a card to change what appears here." Resolved per read (singleton VM).</summary>
    public string CardHintText => LocalizationManager.Instance.GetString("Today_CardHint");

    /// <summary>Load both cards: which records they hold, and what those records say.</summary>
    public async Task LoadStatCardsAsync()
    {
        var pet = ActivePet;
        var generation = ++_cardGeneration;

        var config = await _todayCards.GetConfigAsync(pet);
        var primary = await _todayCards.GetReadingAsync(pet, config.Primary);
        var secondary = await _todayCards.GetReadingAsync(pet, config.Secondary);
        var discovered = await _SettingsService.GetFlagAsync(SettingsFlags.TodayCardsDiscovered);

        // A newer load owns the cards now (a pet switch, or a second appearance).
        if (generation != _cardGeneration)
            return;

        PrimaryCard.Apply(config.Primary, primary);
        SecondaryCard.Apply(config.Secondary, secondary);
        ShowCardHint = !discovered && !_cardsDiscovered;
    }

    /// <summary>A card was tapped — anywhere on it. Opens the picker for that slot and
    /// retires the first-run hint: finding the picker once is what it was there for,
    /// whether or not the owner ends up changing anything.</summary>
    private void OnCardTapped(TodayCardItem card)
    {
        MarkCardsDiscoveredAsync().Forget();
        _cardPicker.OpenAsync(ActivePet, card.Slot).Forget();
    }

    private async Task MarkCardsDiscoveredAsync()
    {
        if (_cardsDiscovered)
            return;

        _cardsDiscovered = true;
        ShowCardHint = false;
        await _SettingsService.SetFlagAsync(SettingsFlags.TodayCardsDiscovered, true);
    }

    // ── Today's care: the avatar ring + next-up card ─────────────────────
    // Both are derived from the SAME PendingEngine snapshot the Journal's
    // "Still to do" chips use — always evaluated for TODAY and the active pet,
    // never for the Journal's parked date selection.

    private double careProgress;
    /// <summary>Fraction of today's care done (0..1); drives the avatar ring.
    /// 0 when nothing is scheduled or tracked at all.</summary>
    public double CareProgress
    {
        get => careProgress;
        set => SetProperty(ref careProgress, value);
    }

    /// <summary>The single next thing to do today — pending med doses first
    /// (soonest due), then care-plan trackers — or null when everything's done.
    /// Consumed by MainPage's next-up card from code-behind.</summary>
    public PendingItem? NextUpItem { get; private set; }

    /// <summary>Dosage line for a medication next-up ("2 mg"); empty for trackers.</summary>
    public string NextUpDetail { get; private set; } = string.Empty;

    /// <summary>The owner's own tracker behind <see cref="NextUpItem"/>, when that is a
    /// custom one; null otherwise.
    ///
    /// <para>Resolved HERE because the card's name and emoji live on a database row, and
    /// the page reads no store. Without it the card falls to TrackerVisuals.Fallback,
    /// whose label key is the mood one — a walk would show up on Today as "Mood".</para></summary>
    public CustomTracker? NextUpCustom { get; private set; }

    /// <summary>Re-read everything on this page that comes from the clock rather than
    /// the database. Runs on every appearance, including the ones that skip the reload:
    /// the hour can roll past noon or 6pm while the page sits on another tab.</summary>
    public void RefreshClockDerived() => OnPropertyChanged(nameof(Greeting));

    public async Task LoadTodayCareAsync()
    {
        // Cheap, and this runs on every appearance — so a page left open across
        // noon or 6pm picks up the right greeting when it comes back.
        RefreshClockDerived();

        if (ActivePet == null)
        {
            CareProgress = 0;
            NextUpItem = null;
            NextUpDetail = string.Empty;
            return;
        }

        var care = await _pendingItems.GetTodayCareAsync(ActivePet, DateTime.Now.Date);
        CareProgress = care.Progress.Total == 0 ? 0 : (double)care.Progress.Done / care.Progress.Total;
        NextUpItem = care.Pending.FirstOrDefault();

        // The pending item carries only the med's identity; the card also shows
        // the dosage, which lives on the medication row.
        NextUpDetail = string.Empty;
        NextUpCustom = null;
        if (NextUpItem is { Kind: PendingKind.Medication } dose)
        {
            var med = await _medicationService.GetMedicationByIdAsync(dose.MedicationId);
            if (med != null)
                NextUpDetail = $"{med.Dosage} {med.Unit}";
        }
        else if (NextUpItem?.Tracker is { IsCustom: true } key)
        {
            NextUpCustom = await _customTrackers.GetByIdAsync(key.CustomId);
        }
    }

    // ── Reminder health (AI/design-decisions.md → notifications) ────────────────
    //
    // The runtime notification permission can be declined at the prompt or switched
    // off in system settings months later, and the OS then simply refuses everything
    // the app schedules. Without this the carer's reminders just stop, silently and
    // permanently — the worst failure this product has. So the Today page states the
    // fact, once, wherever it's true, and offers the one action that fixes it.
    //
    // Only surfaced when the carer actually has reminders set up: telling someone
    // with no medications that their notifications are off is noise, not help.

    private bool _remindersBlocked;

    /// <summary>True when reminders are configured but the OS won't deliver them.</summary>
    public bool RemindersBlocked
    {
        get => _remindersBlocked;
        private set => SetProperty(ref _remindersBlocked, value);
    }

    /// <summary>Opens this app's system notification settings — the only place the
    /// carer can undo a denial, since Android stops showing the prompt after two.</summary>
    public ICommand OpenNotificationSettingsCommand { get; } = new Command(() =>
    {
        try { AppInfo.Current.ShowSettingsUI(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MainPage] settings open failed: {ex.Message}"); }
    });

    // ── The visit that is near ───────────────────────────────────────────────
    //
    // The appointment is a STATE, not a destination (see Data/Models/VetVisit.cs):
    // it lives on the Care page and only reaches Today when it is close. Seven days,
    // fixed — near enough to be worth saying, far enough to still be useful.

    /// <summary>How close a visit has to be before Today says anything about it.</summary>
    private const int NearVisitDays = 7;

    private bool _hasNearVisit;

    /// <summary>A visit is scheduled within the next week.</summary>
    public bool HasNearVisit
    {
        get => _hasNearVisit;
        private set => SetProperty(ref _hasNearVisit, value);
    }

    private string _nearVisitLine = string.Empty;

    /// <summary>"Charly sees Dr. Weiss on Thursday, 15:30" — or without the vet, or
    /// without the time, depending on what the owner actually told the app. It states
    /// the fact and stops: no countdown, no "in 2 days", nothing that grows louder as
    /// the date approaches.</summary>
    public string NearVisitLine
    {
        get => _nearVisitLine;
        private set => SetProperty(ref _nearVisitLine, value);
    }

    /// <summary>The band is a door. The page owns navigation, so the VM raises and the
    /// code-behind pushes — the same split every other pushed page here uses.</summary>
    public event Action? AppointmentRequested;

    public ICommand OpenAppointmentCommand { get; }

    /// <summary>
    /// Re-read the active pet's next visit. Cheap — one indexed range query — and run on
    /// every appearance, because "within seven days" changes by itself at midnight.
    /// </summary>
    public async Task RefreshNearVisitAsync()
    {
        try
        {
            var pet = ActivePet;
            var next = pet is null || pet.Id == 0 ? null : await _vetVisits.GetNextAsync(pet.Id);

            if (next is null || (next.Date.Date - DateTime.Today).TotalDays > NearVisitDays)
            {
                HasNearVisit = false;
                NearVisitLine = string.Empty;
                return;
            }

            NearVisitLine = BuildNearVisitLine(pet!.Name, next);
            HasNearVisit = true;
        }
        catch (Exception ex)
        {
            // A failed read must never light the band falsely.
            System.Diagnostics.Debug.WriteLine($"[MainPage] next visit check failed: {ex.Message}");
            HasNearVisit = false;
        }
    }

    /// <summary>Whatever the owner did not give is simply absent — never a placeholder
    /// and never a guessed hour (the same rule as an unknown birth month).</summary>
    private static string BuildNearVisitLine(string petName, Data.Models.VetVisit visit)
    {
        var loc = LocalizationManager.Instance;
        var day = visit.Date.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture);
        var when = visit.Time is TimeSpan t ? $"{day}, {t:hh\\:mm}" : day;

        var who = !string.IsNullOrWhiteSpace(visit.VetName) ? visit.VetName.Trim()
            : !string.IsNullOrWhiteSpace(visit.Practice) ? visit.Practice.Trim()
            : string.Empty;

        return who.Length > 0
            ? loc.Format("Today_VisitLineWho", petName, who, when)
            : loc.Format("Today_VisitLine", petName, when);
    }

    /// <summary>
    /// Re-check whether the OS will deliver reminders. A live check on every
    /// appearance and resume, never a cached setup flag — the permission can be
    /// revoked without the app running.
    /// </summary>
    public async Task RefreshReminderHealthAsync()
    {
        try
        {
            // Short-circuits before touching the database when the daily reminder is on,
            // and asks for a COUNT rather than every medication row when it isn't — this
            // runs on every appearance, including the ones that skip the rest of the load.
            var hasReminders = DailyCareReminderSettings.Enabled
                || await _medicationService.AnyActiveMedicationsAsync();

            RemindersBlocked = hasReminders && !await _reminderScheduler.AreRemindersDeliverableAsync();
        }
        catch (Exception ex)
        {
            // A failed probe must never light the banner falsely.
            System.Diagnostics.Debug.WriteLine($"[MainPage] reminder health check failed: {ex.Message}");
            RemindersBlocked = false;
        }
    }

    /// <summary>Record the current next-up medication dose as taken (the card's
    /// one-tap action), then re-derive the ring + next-up. Same dose-log write and
    /// reminder bookkeeping as the Journal's chip tap.</summary>
    public async Task MarkNextDoseGivenAsync()
    {
        if (NextUpItem is not { Kind: PendingKind.Medication } dose)
            return;

        var today = DateTime.Now.Date;
        var time = dose.DoseTime ?? TimeSpan.Zero;
        await _doseLogService.SetStatusAsync(dose.MedicationId, dose.PetId, today, time, DoseStatus.Taken);
        // Stop this occurrence's reminder from firing late or being re-sent.
        await _reminderScheduler.MarkDoseHandledAsync(dose.MedicationId, today, time);

        await LoadTodayCareAsync();
    }
}