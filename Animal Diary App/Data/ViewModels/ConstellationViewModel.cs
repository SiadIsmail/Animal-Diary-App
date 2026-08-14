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
    private readonly IAnalyticsService _analytics;

    /// <summary>The offered stretches. Presets only, like the vet report's look-backs —
    /// a date-range picker is a form, and this is a thing to look at.</summary>
    public static readonly int[] RangeOptions = { 7, 30, 90, 365 };

    private int _rangeDays = 30;
    private bool _isLoading;
    private int _selectedIndex = -1;
    private int _loadGeneration;
    private SkyLens _lens = SkyLens.Timeline;
    private CelestialCategory? _focus;
    private double _periodDays = 7;

    public ConstellationViewModel(
        ConstellationService constellation,
        ActivePetService activePet,
        IAnalyticsService analytics)
    {
        _constellation = constellation;
        _activePet = activePet;
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

    /// <summary>Raised when the arrangement changed but the DATA did not — a lens, a
    /// fold, a focus. The page re-places and repaints without going near the database.</summary>
    public event Action? ViewChanged;

    // ── The lenses ───────────────────────────────────────────────────────────────

    public SkyLens Lens
    {
        get => _lens;
        private set
        {
            if (!SetProperty(ref _lens, value))
                return;

            OnPropertyChanged(nameof(IsTimeline));
            OnPropertyChanged(nameof(IsClock));
            OnPropertyChanged(nameof(IsRhythm));
            OnPropertyChanged(nameof(Hint));
            SelectedIndex = -1;
            ViewChanged?.Invoke();
        }
    }

    public bool IsTimeline => Lens == SkyLens.Timeline;
    public bool IsClock => Lens == SkyLens.Clock;
    public bool IsRhythm => Lens == SkyLens.Rhythm;

    /// <summary>How far the Rhythm lens folds time, in days. <b>The owner sets this and
    /// the app never does</b> — Felova offers a dial and says nothing whatsoever about
    /// what lines up on it. Suggesting a period would be the app claiming a pattern,
    /// which is exactly the claim it cannot support.</summary>
    public double PeriodDays
    {
        get => _periodDays;
        set
        {
            var rounded = Math.Round(Math.Clamp(value, 2, 60));
            if (!SetProperty(ref _periodDays, rounded))
                return;

            OnPropertyChanged(nameof(PeriodLabel));
            if (IsRhythm)
                ViewChanged?.Invoke();
        }
    }

    public string PeriodLabel => Loc.Format("Sky_FoldEvery", (int)PeriodDays);

    /// <summary>What this lens is for, said in one line. The Rhythm lens in particular
    /// is useless without it — a fold dial explains nothing about itself.</summary>
    public string Hint => Loc.GetString(Lens switch
    {
        SkyLens.Clock => "Sky_HintClock",
        SkyLens.Rhythm => "Sky_HintRhythm",
        _ => "Sky_HintTimeline",
    });

    // ── Focus ────────────────────────────────────────────────────────────────────

    /// <summary>The one kind brought forward, or null for all of them.</summary>
    public CelestialCategory? Focus
    {
        get => _focus;
        private set
        {
            if (!SetProperty(ref _focus, value))
                return;

            foreach (var item in Legend)
                item.IsFocused = value == item.Category;

            ViewChanged?.Invoke();
        }
    }

    /// <summary>Tap a legend key to bring that kind forward; tap it again to let the
    /// whole sky back in.</summary>
    private void ToggleFocus(CelestialLegendItem? item)
    {
        if (item is null)
            return;

        Focus = Focus == item.Category ? null : item.Category;
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

    /// <summary>Everything decorative that belongs to this pet — the wave's shape, the
    /// starfield's seed, the atmosphere's colour. Rebuilt on load because the active
    /// pet can change under the page.</summary>
    public SkySignature Signature { get; private set; } = SkySignature.Default;

    /// <summary>This pet's own figure of stars, drawn from their name. Decoration, and
    /// nothing but — see <see cref="ConstellationAsterism"/> for the line it must not
    /// cross.</summary>
    public Asterism Asterism { get; private set; } = Asterism.Empty;

    /// <summary>"Charly's constellation" — the figure's name, shown quietly in the
    /// corner of the sky. It is what makes a screenshot of this mean something to
    /// someone who wasn't told what they are looking at.</summary>
    public string AsterismName => Asterism.HasShape
        ? Loc.Format("Sky_AsterismName", PetName)
        : string.Empty;

    public bool HasAsterism => Asterism.HasShape;

    /// <summary>The title on a shared picture: the figure's name, or the pet's own if
    /// they have no figure yet.</summary>
    public string ShareTitle => Asterism.HasShape ? AsterismName : PetName;

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

    /// <summary>True while the sky is made up. Drives an unmissable banner: nothing in
    /// this app may show invented medical history without saying so, however
    /// temporary the build is meant to be.</summary>
    public bool IsSampleSky => ConstellationConfig.UseSampleSky;

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

            // The development switch (AI: ConstellationConfig.UseSampleSky ships false).
            // It replaces the read entirely rather than adding to it — a sky that mixed
            // invented history with someone's real records would be unreadable, and
            // worse, indistinguishable.
            var events = ConstellationConfig.UseSampleSky
                ? ConstellationSampleSky.Generate(from, to)
                : pet is null || pet.Id == 0
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
            // The name overload, not the signature one: a pet with no name yet gets the
            // default sky but NO figure, because a figure is the name made visible and
            // there is nothing to make visible.
            Asterism = ConstellationAsterism.For(pet?.Name, pet?.BirthYear ?? 0);

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
        OnPropertyChanged(nameof(AsterismName));
        OnPropertyChanged(nameof(HasAsterism));
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
                IsFocused = _focus == category,
            });
        }

        // A kind that is no longer in the sky cannot stay in focus — changing the range
        // would otherwise leave the whole page dimmed for something with nothing in it.
        if (_focus is CelestialCategory kind && !present.Contains(kind))
            _focus = null;

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
        await LoadAsync();
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
