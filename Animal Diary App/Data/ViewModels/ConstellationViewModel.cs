namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

// ─────────────────────────────────────────────────────────────────────────────
//  The Constellation's data half.
//
//  It owns the stretch of time being looked at, the events in it, the legend, and
//  whichever star was tapped. It owns NO geometry: where a star lands is
//  ConstellationLayout's answer and the page asks for it, because only the page
//  knows how big the canvas is (same split as the photo cropper — four numbers here,
//  pixels there).
//
//  Nothing in this class ranks, totals, averages or compares. The only number it
//  produces is a count of what was written down, which is a fact about the records
//  rather than a claim about the animal.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One row of the legend: a symbol, its colour, its name — and, since the
/// legend is also how a kind is brought into focus, whether it is the chosen one.</summary>
public class CelestialLegendItem : BaseViewModel
{
    private bool _isFocused;

    public CelestialCategory Category { get; init; }
    public string Label { get; init; } = string.Empty;

    /// <summary>This kind is the one in focus. The legend stops being a key and starts
    /// being a control the moment you can tap it, which is the cheapest large
    /// improvement available to a sky with eight kinds in it.</summary>
    public bool IsFocused
    {
        get => _isFocused;
        set => SetProperty(ref _isFocused, value);
    }

    /// <summary>The very same drawing routine the sky uses, at legend size — a legend
    /// that could drift from the thing it explains would be worse than none.</summary>
    public IDrawable Symbol { get; init; } = new View.Controls.CelestialSymbolDrawable();

    /// <summary>The alternating degree-or-two tilt every icon tile in this app wears
    /// (see <c>TimelineItem.IconRotation</c>) — imperfection on the frame.</summary>
    public double Tilt { get; init; }
}

public class ConstellationViewModel : BaseViewModel
{
    private readonly ConstellationService _constellation;
    private readonly ActivePetService _activePet;
    private readonly PetConditionService _conditions;
    private readonly IAnalyticsService _analytics;

    /// <summary>The offered stretches. Presets only, like the vet report's look-backs —
    /// a date-range picker is a form, and this is a thing to look at.</summary>
    public static readonly int[] RangeOptions = { 7, 30, 90, 365 };

    private int _rangeDays = 30;
    private bool _isLoading;
    private int _selectedIndex = -1;
    private int _loadGeneration;
    private SkyLens _lens = SkyLens.History;
    private readonly List<CelestialCategory> _focus = new();

    /// <summary>Past three, "focused" stops meaning anything — it is just the sky
    /// again with two kinds missing.</summary>
    private const int MaxFocus = 3;

    /// <summary>The owner has picked a focus themselves, so stop deriving one. Same
    /// "seeded once, then owned" shape the care plan and Today's cards use — a derived
    /// default is only a default until there is an intent to protect.</summary>
    private bool _focusChosen;
    private double _periodDays = 1;

    public ConstellationViewModel(
        ConstellationService constellation,
        ActivePetService activePet,
        PetConditionService conditions,
        IAnalyticsService analytics)
    {
        _constellation = constellation;
        _activePet = activePet;
        _conditions = conditions;
        _analytics = analytics;

        SetRangeCommand = new Command<string>(async days => await SetRangeAsync(days));
        ClearSelectionCommand = new Command(() => SelectedIndex = -1);
        SetLensCommand = new Command<string>(SetLens);
        ToggleFocusCommand = new Command<CelestialLegendItem>(ToggleFocus);
    }

    private static LocalizationManager Loc => LocalizationManager.Instance;

    /// <summary>Raised once a load has replaced <see cref="Events"/> — the page
    /// re-places the sky and invalidates. An event rather than a bound collection
    /// because the canvas is not a list and has nothing to bind to.</summary>
    public event Action? SkyChanged;

    public ICommand SetRangeCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand SetLensCommand { get; }
    public ICommand ToggleFocusCommand { get; }

    /// <summary>Raised when the ARRANGEMENT changed but the data did not — a lens or a
    /// fold. The page re-places and flies the stars to their new positions without
    /// going near the database.</summary>
    public event Action? ViewChanged;

    /// <summary>Raised when only the PAINTING changed — the focus. Deliberately not
    /// <see cref="ViewChanged"/>: re-placing for a focus is both wasted work and, worse,
    /// it used to reset the camera, so tapping a legend key threw the wall of nights
    /// back to its bottom and lost the Timeline's zoom. Nobody moved; someone got
    /// brighter.</summary>
    public event Action? RepaintRequested;

    // ── The lenses ───────────────────────────────────────────────────────────────

    public SkyLens Lens
    {
        get => _lens;
        private set
        {
            if (!SetProperty(ref _lens, value))
                return;

            OnPropertyChanged(nameof(IsHistory));
            OnPropertyChanged(nameof(IsCycle));
            OnPropertyChanged(nameof(Hint));
            OnPropertyChanged(nameof(LensDescription));
            SelectedIndex = -1;
            ViewChanged?.Invoke();
        }
    }

    public bool IsHistory => Lens == SkyLens.History;
    public bool IsCycle => Lens == SkyLens.Cycle;

    /// <summary>How far the Cycle ring folds time, in days. <b>The owner sets this and
    /// the app never does</b> — Felova offers a dial and says nothing whatsoever about
    /// what lines up on it. Suggesting a period would be the app claiming a pattern,
    /// which is exactly the claim it cannot support.</summary>
    public double PeriodDays
    {
        get => _periodDays;
        set
        {
            var rounded = Math.Round(Math.Clamp(value, 1, MaxPeriodDays));
            if (!SetProperty(ref _periodDays, rounded))
                return;

            OnPropertyChanged(nameof(PeriodLabel));
            if (IsCycle)
                ViewChanged?.Invoke();
        }
    }

    /// <summary>
    /// The longest fold worth offering: <b>half the stretch on screen</b>.
    ///
    /// <para>You cannot see a repeat in a window that does not hold at least two of
    /// them, so beyond this the lens has nothing to show — and folding a seven-day
    /// range at sixty days crammed every entry into the leftmost tenth of the card and
    /// simply looked broken. This is a mechanical bound, not the app choosing a period:
    /// it says what the picture is capable of showing, never what the answer is.</para>
    /// </summary>
    public double MaxPeriodDays => Math.Max(2, Math.Min(60, RangeDays / 2));

    /// <summary>What the fold is, said the way a person would say it. "Every 1 days" is
    /// nobody's sentence — a one-day fold is a clock face and a seven-day fold is a
    /// week, and both deserve their own words.</summary>
    public string PeriodLabel => (int)PeriodDays switch
    {
        1 => Loc.GetString("Sky_FoldDayLabel"),
        7 => Loc.GetString("Sky_FoldWeekLabel"),
        _ => Loc.Format("Sky_FoldEvery", (int)PeriodDays),
    };

    /// <summary>What this lens is for, said in one line.</summary>
    public string Hint => Loc.GetString(Lens switch
    {
        SkyLens.Cycle => "Sky_HintCycle",
        _ => "Sky_HintHistory",
    });

    /// <summary>
    /// Shown alongside the lens hint whenever there is more than one kind to choose
    /// between.
    ///
    /// <para>Not a retiring hint. The Constellation is not a daily surface — someone
    /// opens it before an appointment, which might be twice a year — so "they will have
    /// learned it by now" is an assumption about a habit nobody has. Both lines stay,
    /// and they sit at the top where they are read before the picture rather than
    /// explaining it afterwards.</para>
    /// </summary>
    public bool ShowFocusHint => Legend.Count > 1;

    /// <summary>What the canvas is, for a screen reader — which cannot see a sky. It
    /// names the arrangement and how much is in it, which is everything the picture
    /// itself claims.</summary>
    public string LensDescription => Loc.Format(Lens switch
    {
        SkyLens.Cycle => "Sky_A11yCycle",
        _ => "Sky_A11yHistory",
    }, Events.Count);

    // ── Focus ────────────────────────────────────────────────────────────────────

    /// <summary>The kinds brought forward, or empty for all of them.</summary>
    public IReadOnlyList<CelestialCategory> Focus => _focus;

    /// <summary>
    /// Tap a legend key to bring that kind forward; tap it again to let it back into
    /// the crowd.
    ///
    /// <para><b>Up to three at once</b>, because the question an owner actually has is
    /// usually about two things — the nights he seized, against the nights the evening
    /// dose went in. Both are drawn at their real times and nothing is overlaid or
    /// computed; the conclusion is the owner's to draw, exactly as it would be turning
    /// the pages of a paper diary.</para>
    /// </summary>
    private void ToggleFocus(CelestialLegendItem? item)
    {
        if (item is null)
            return;

        _focusChosen = true;

        if (!_focus.Remove(item.Category))
        {
            // At the cap, the oldest choice makes way — so a tap always does something
            // rather than silently refusing.
            if (_focus.Count >= MaxFocus)
                _focus.RemoveAt(0);

            _focus.Add(item.Category);
        }

        foreach (var key in Legend)
            key.IsFocused = _focus.Contains(key.Category);

        RepaintRequested?.Invoke();
    }

    private void SetLens(string? lens)
    {
        if (Enum.TryParse<SkyLens>(lens, out var value))
            Lens = value;
    }

    /// <summary>Everything recorded in the range, ascending by time. Replaced whole on
    /// every load; never mutated in place.</summary>
    public IReadOnlyList<CelestialEvent> Events { get; private set; } = Array.Empty<CelestialEvent>();

    public ObservableCollection<CelestialLegendItem> Legend { get; } = new();

    /// <summary>Inclusive bounds of the sky. <see cref="To"/> is the end of today, so
    /// something written down an hour ago is inside the frame rather than on its edge.</summary>
    public DateTime From { get; private set; } = DateTime.Now.Date;
    public DateTime To { get; private set; } = DateTime.Now.Date.AddDays(1);

    public int RangeDays
    {
        get => _rangeDays;
        private set => SetProperty(ref _rangeDays, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string PetName => _activePet.ActivePet?.Name ?? string.Empty;

    /// <summary>The one decorative thing that belongs to this pet: the colour its
    /// atmosphere leans towards. Rebuilt on load because the active pet can change
    /// under the page.</summary>
    public SkySignature Signature { get; private set; } = SkySignature.Default;

    /// <summary>The title on a shared picture.</summary>
    public string ShareTitle => Loc.Format("Sky_ShareTitle", PetName);

    /// <summary>The line under it — the stretch, and how much is in it. Both facts
    /// about the records; nothing about the animal.</summary>
    public string ShareSubtitle => $"{Loc.Format("Sky_ShareRange", RangeDays)} · {CountLine}";

    /// <summary>How much is in the sky. A count of records is a fact about the diary,
    /// not a reading of the animal — the same footing the vet report's counts stand on
    /// (AI/domain.md).</summary>
    public string CountLine => Events.Count == 1
        ? Loc.GetString("Sky_CountOne")
        : Loc.Format("Sky_CountMany", Events.Count);

    public bool IsEmpty => !IsLoading && Events.Count == 0;

    // ── The tapped star ──────────────────────────────────────────────────────────

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (!SetProperty(ref _selectedIndex, value))
                return;

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedTitle));
            OnPropertyChanged(nameof(SelectedDetail));
            OnPropertyChanged(nameof(HasSelectedDetail));
            OnPropertyChanged(nameof(SelectedWhen));
            OnPropertyChanged(nameof(SelectedCategoryLabel));
            OnPropertyChanged(nameof(SelectedSymbol));
            OnPropertyChanged(nameof(SelectedGap));
            OnPropertyChanged(nameof(HasSelectedGap));
            BuildSameDay();
        }
    }

    public bool HasSelection => Selected is not null;

    private CelestialEvent? Selected =>
        _selectedIndex >= 0 && _selectedIndex < Events.Count ? Events[_selectedIndex] : null;

    /// <summary>What it was. For an owner-defined tracker this is the owner's own name
    /// for it, shown verbatim — never translated, never fitted into a sentence.</summary>
    public string SelectedTitle => Selected?.Title ?? string.Empty;

    /// <summary>The reading exactly as it was written down, or empty. This is the one
    /// place a value appears at all: read out because a finger asked, never drawn into
    /// the sky where it would become a second thing the picture encodes.</summary>
    public string SelectedDetail => Selected?.Detail ?? string.Empty;
    public bool HasSelectedDetail => !string.IsNullOrWhiteSpace(SelectedDetail);

    public string SelectedWhen => Selected is CelestialEvent e
        ? e.When.ToString(Loc.GetString("Sky_WhenFormat"), System.Globalization.CultureInfo.CurrentCulture)
        : string.Empty;

    public string SelectedCategoryLabel => Selected is CelestialEvent e
        ? CelestialVisuals.Label(e.Category)
        : string.Empty;

    /// <summary>
    /// How long after the previous entry <b>of the same kind</b> this one was written
    /// down — "18 days after the last one".
    ///
    /// <para>It is the answer to "does this come round?" delivered with no chart at
    /// all, and it is the one number on this surface that could have been a streak. It
    /// is not, and the reason is that it applies to <b>every kind equally</b>: a mood, a
    /// weigh-in and a seizure all get the same sentence. A counter that appeared only
    /// on seizures would be "days since", which is a thing the owner can break by
    /// writing down the truth — the exact reason streaks are banned from Today's cards
    /// (AI/domain.md). A gap between two entries that both already happened is a fact
    /// about the diary, and it reads as one because nothing about it is special.</para>
    ///
    /// <para>Empty for the first of its kind in the range: the previous one may well
    /// exist just outside the stretch being looked at, and the app does not guess.</para>
    /// </summary>
    public string SelectedGap
    {
        get
        {
            if (_selectedIndex <= 0 || _selectedIndex >= Events.Count)
                return string.Empty;

            var current = Events[_selectedIndex];
            for (int i = _selectedIndex - 1; i >= 0; i--)
            {
                if (Events[i].Category != current.Category)
                    continue;

                return DescribeGap(current.When - Events[i].When);
            }

            return string.Empty;
        }
    }

    public bool HasSelectedGap => !string.IsNullOrEmpty(SelectedGap);

    /// <summary>
    /// What else was written down on the same day as the tapped entry.
    ///
    /// <para>This is the useful half of "did anything go with it" — and it is useful
    /// precisely because it does <b>nothing</b> but recall. It lists the day's other
    /// entries at their own times and stops. No window, no overlay, no ordering by
    /// relevance, no suggestion that any of it is connected. An owner reading "the night
    /// he seized, he had eaten nothing since morning" has noticed something worth saying
    /// to a vet; Felova has only turned the page for them.</para>
    ///
    /// <para>Capped, and the cap is not a summary — on a day with more entries than fit,
    /// the line underneath says how many are not shown rather than choosing which
    /// matter.</para>
    /// </summary>
    public ObservableCollection<string> SameDay { get; } = new();

    public bool HasSameDay => SameDay.Count > 0;

    /// <summary>"and 4 more", or empty. Never a selection of which four.</summary>
    public string SameDayMore { get; private set; } = string.Empty;
    public bool HasSameDayMore => !string.IsNullOrEmpty(SameDayMore);

    private const int SameDayLimit = 5;

    private void BuildSameDay()
    {
        var lines = new List<string>(SameDayLimit);
        var extra = 0;

        if (Selected is CelestialEvent chosen)
        {
            var day = chosen.When.Date;
            for (int i = 0; i < Events.Count; i++)
            {
                if (i == _selectedIndex || Events[i].When.Date != day)
                    continue;

                if (lines.Count < SameDayLimit)
                    lines.Add($"{Events[i].When:HH\\:mm} · {Events[i].Title}");
                else
                    extra++;
            }
        }

        // Built first, then swapped in one synchronous block — never cleared across an
        // await (AI/coding-standards.md).
        SameDay.Clear();
        foreach (var line in lines)
            SameDay.Add(line);

        SameDayMore = extra > 0 ? Loc.Format("Sky_AlsoMore", extra) : string.Empty;

        OnPropertyChanged(nameof(HasSameDay));
        OnPropertyChanged(nameof(SameDayMore));
        OnPropertyChanged(nameof(HasSameDayMore));
    }

    /// <summary>Minutes, hours or days — whichever a person would actually say. Two
    /// seizures twenty minutes apart is a very different sentence from two eighteen
    /// days apart, and rounding the first to "0 days" would throw away the thing worth
    /// noticing.</summary>
    private static string DescribeGap(TimeSpan gap)
    {
        if (gap <= TimeSpan.Zero)
            return string.Empty;

        if (gap.TotalMinutes < 90)
        {
            var minutes = Math.Max(1, (int)Math.Round(gap.TotalMinutes));
            return Loc.Format(minutes == 1 ? "Sky_GapMinuteOne" : "Sky_GapMinutes", minutes);
        }

        if (gap.TotalHours < 36)
        {
            var hours = (int)Math.Round(gap.TotalHours);
            return Loc.Format(hours == 1 ? "Sky_GapHourOne" : "Sky_GapHours", hours);
        }

        var days = (int)Math.Round(gap.TotalDays);
        return Loc.Format(days == 1 ? "Sky_GapDayOne" : "Sky_GapDays", days);
    }

    /// <summary>The tapped star, redrawn at tile size on the sheet — so the thing being
    /// described is recognisably the thing that was touched.</summary>
    public IDrawable? SelectedSymbol => Selected is CelestialEvent e ? SymbolFor(e.Category) : null;

    private static View.Controls.CelestialSymbolDrawable SymbolFor(CelestialCategory category) => new()
    {
        Category = category,
        Color = AppColors.Resolve(CelestialVisuals.For(category).ColorKey, Colors.White),
    };

    // ── Loading ──────────────────────────────────────────────────────────────────

    /// <summary>Read the range for the active pet. Safe to call on every appearance and
    /// on RemoteChangesApplied; a stale load never paints over a newer one.</summary>
    public async Task LoadAsync()
    {
        var generation = ++_loadGeneration;
        var pet = _activePet.ActivePet;

        IsLoading = true;
        try
        {
            // The end is the end of TODAY, not "now": a range that stopped at the
            // current minute would leave this evening's entries pinned to the very
            // edge of the canvas with nowhere to sit.
            var to = DateTime.Now.Date;
            var from = to.AddDays(-(RangeDays - 1));

            // One read path, always. The compile-time sample-sky switch that used to sit
            // here is gone: a demo pet is a real pet with real rows, so it arrives through
            // this same query and there is nothing left to branch on.
            var events = pet is null || pet.Id == 0
                ? new List<CelestialEvent>()
                : await _constellation.GetRangeAsync(pet.Id, from, to);

            // A newer range (or a newer pet) owns the sky now — this result is history.
            if (generation != _loadGeneration)
                return;

            From = from;
            To = to.AddDays(1);
            Events = events;
            SelectedIndex = -1;

            // Derived from who the pet IS, never from what is in the sky — so it is
            // identical whether this is their first day or their fifth year.
            Signature = SkySignature.For(pet?.Name, pet?.BirthYear ?? 0);

            // Volume is not importance: a quarter of twice-daily doses is 180 entries
            // against six seizures. Opening on the kind the pet's conditions suggest is
            // the honest way to keep the rare thing findable — see
            // CelestialVisuals.OpeningFocusFor.
            if (!_focusChosen)
            {
                _focus.Clear();
                if (CelestialVisuals.OpeningFocusFor(await _conditions.GetConditionIdsAsync(pet))
                    is CelestialCategory opening)
                {
                    _focus.Add(opening);
                }
            }

            BuildLegend();
        }
        finally
        {
            if (generation == _loadGeneration)
                IsLoading = false;
        }

        OnPropertyChanged(nameof(PetName));
        OnPropertyChanged(nameof(CountLine));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(LensDescription));
        OnPropertyChanged(nameof(ShowFocusHint));
        OnPropertyChanged(nameof(ShareTitle));
        OnPropertyChanged(nameof(ShareSubtitle));
        SkyChanged?.Invoke();
    }

    /// <summary>Only the kinds actually present. A legend is a key to what is on the
    /// canvas; listing seizures for a pet that has never had one would answer a
    /// question nobody asked, on a screen someone opened to look at their animal.</summary>
    private void BuildLegend()
    {
        var present = new HashSet<CelestialCategory>();
        foreach (var e in Events)
            present.Add(e.Category);

        var items = new List<CelestialLegendItem>(present.Count);
        foreach (var category in CelestialVisuals.All)
        {
            if (!present.Contains(category))
                continue;

            items.Add(new CelestialLegendItem
            {
                Category = category,
                Label = CelestialVisuals.Label(category),
                Symbol = SymbolFor(category),
                Tilt = items.Count % 2 == 0 ? -3 : 2.5,
                IsFocused = _focus.Contains(category),
            });
        }

        // A kind that is no longer in the sky cannot stay in focus — changing the range
        // would otherwise leave the whole page dimmed for something with nothing in it.
        _focus.RemoveAll(kind => !present.Contains(kind));

        // Built first, then swapped in one synchronous block — never cleared across an
        // await (AI/coding-standards.md).
        Legend.Clear();
        foreach (var item in items)
            Legend.Add(item);
    }

    private async Task SetRangeAsync(string? days)
    {
        if (!int.TryParse(days, out var value) || value == RangeDays)
            return;

        RangeDays = value;
        OnPropertyChanged(nameof(MaxPeriodDays));
        // A fold longer than half the new stretch can show nothing; pull it back in
        // rather than leaving the lens looking broken.
        PeriodDays = Math.Clamp(PeriodDays, 1, MaxPeriodDays);
        await LoadAsync();
    }

    /// <summary>
    /// The page has been left. Forget what this visit explored.
    ///
    /// <para>This ViewModel is a DI <b>singleton</b>, so without it a focus picked in
    /// one visit is still there in the next — and worse, the "the owner has chosen"
    /// flag stays set too, which permanently defeats the condition-derived opening
    /// focus for the rest of the process. Someone who once tapped Appetite would never
    /// again open on their dog's seizures.</para>
    ///
    /// <para>Focus is <b>exploration state, not a preference</b>: it is never persisted,
    /// nothing writes it down, and every visit is meant to start from the same place —
    /// the kind the pet's conditions suggest. Deliberately narrow: the range, the lens
    /// and the fold are left alone, because those are how the owner asked to look at the
    /// data rather than what the app decided to show them.</para>
    /// </summary>
    public void EndVisit()
    {
        _focusChosen = false;
        _focus.Clear();
        SelectedIndex = -1;

        foreach (var key in Legend)
            key.IsFocused = false;
    }

    /// <summary>Fired once per visit by the page. Coarse and anonymous: which stretch
    /// was being looked at, never what is in it (AI/analytics.md).</summary>
    public void TrackOpened() =>
        _analytics.Track(AnalyticsEvents.ConstellationOpened, new Dictionary<string, object?>
        {
            [AnalyticsEvents.PropRangeDays] = RangeDays,
        });

    /// <summary>A picture of the sky reached the share sheet. The one signal that says
    /// whether this surface does the job it was built for — never the pet, the name, or
    /// what is in the picture.</summary>
    public void TrackShared() =>
        _analytics.Track(AnalyticsEvents.ConstellationShared, new Dictionary<string, object?>
        {
            [AnalyticsEvents.PropRangeDays] = RangeDays,
        });
}
