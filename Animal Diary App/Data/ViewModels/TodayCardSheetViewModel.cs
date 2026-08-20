namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>One row in the card picker. Its name and icon come from the shared card
/// table and are resolved per read, so a live language switch re-translates the open
/// sheet like everything else.</summary>
public class TodayCardOption : BaseViewModel
{
    private readonly string _customName;
    private readonly string _customIcon;

    public TodayCardOption(TodayCardKey card, bool isSelected, bool isOnOtherCard,
        string customName = "", string customIcon = "")
    {
        Card = card;
        IsSelected = isSelected;
        IsOnOtherCard = isOnOtherCard;
        _customName = customName;
        _customIcon = customIcon;
    }

    public TodayCardKey Card { get; }

    public string Icon => Card.IsCustom ? _customIcon : TodayCardCatalog.Visual(Card.BuiltIn!.Value).Icon;

    /// <summary>An owner-defined tracker's row is labelled with its own NAME, held as a
    /// field rather than resolved per read: it is user text, so there is no language for
    /// it to switch into (see AI/coding-standards.md).</summary>
    public string Name => Card.IsCustom ? _customName : TodayCardCatalog.Label(Card.BuiltIn!.Value);

    /// <summary>This is what the tapped card already shows.</summary>
    public bool IsSelected { get; }

    /// <summary>The <b>other</b> card shows this. Picking it swaps the two rather than
    /// showing one record twice — the row says so, so the swap is never a surprise.</summary>
    public bool IsOnOtherCard { get; }

    public string OtherCardNote =>
        IsOnOtherCard ? LocalizationManager.Instance.GetString("Today_PickerOnOtherCard") : string.Empty;
}

/// <summary>
/// What the tapped card's record says, and — below it — what else could sit there.
///
/// <para><b>Two sections, in that order.</b> The facts come first because they are the
/// reason to tap a card more than once: reconfiguring is something an owner does once
/// and never again, so a sheet that only offered the picker had nothing to come back
/// for. The card FACE is untouched by any of this — it states the last recorded value
/// and when, and nothing else, because a count on the face would break the rule that
/// keeps a card from ever reading as a score (AI/domain.md).</para>
///
/// <para>The facts are <see cref="RecordFactsService"/>'s, over a fixed <b>last 90
/// days</b>, with no range picker: a stretch the owner can tune is a control, and this
/// panel is a statement about what they wrote down. Everything in it is arithmetic on
/// their own entries and is computed identically for every record — see the doctrine on
/// <see cref="RecordFacts"/>.</para>
///
/// <para>The picker below is unchanged: one screenful of records with no cadence, no
/// toggles and no ordering. It changes <b>only</b> which record the card displays;
/// nothing here starts, stops or configures any tracking (that is the care plan's job
/// on the Manage page), so a card can show a record the pet's plan doesn't ask for and
/// vice versa.</para>
///
/// <para>Follows the shared sheet contract in AI/coding-standards.md, minus the
/// <c>Saved</c>/undo pair: a display preference is not a journal entry, and picking
/// again is its own undo.</para>
/// </summary>
public class TodayCardSheetViewModel : BaseViewModel
{
    private readonly TodayCardService _cards;
    private readonly CustomTrackerService _custom;
    private readonly RecordFactsService _facts;

    /// <summary>The stretch the facts cover. Fixed, and deliberately not offered as a
    /// choice — see the class summary.</summary>
    private const int FactsDays = 90;

    private Pet? _pet;
    private TodayCardSlot _slot;
    private RecordFacts? _snapshot;
    private string _recordName = string.Empty;

    public TodayCardSheetViewModel(
        TodayCardService cards, CustomTrackerService custom, RecordFactsService facts)
    {
        _cards = cards;
        _custom = custom;
        _facts = facts;

        PickCommand = new Command<TodayCardOption>(async option => await PickAsync(option));
        DismissCommand = new Command(() => IsPresented = false);
    }

    /// <summary>Raised after the pair changed, so Today can re-read both cards.</summary>
    public event Action? Changed;

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    /// <summary>The record's name on its own — "Glucose", not "Last glucose". What sits
    /// under it is ninety days, not the last reading.</summary>
    public string Title => _recordName;

    /// <summary>Empty on purpose: the sheet's subtitle slot is the Caveat handwriting
    /// line, which is a voice. Facts are stated in plain sans in the body, unweighted
    /// and all in one colour.</summary>
    public string Subtitle => string.Empty;

    // ── Section 1: facts about the record ────────────────────────────────────

    /// <summary>There is something to state. False leaves only <see cref="FactsNothing"/>
    /// and the picker.</summary>
    public bool HasFacts => _snapshot?.HasAny == true;

    /// <summary>"84 things written down · last 90 days".</summary>
    public string FactsCount => _snapshot is null ? string.Empty : RecordFactsText.CountAndRange(_snapshot);

    /// <summary>"Night 4 · Morning 61 · Afternoon 12 · Evening 7" — all four bands, in
    /// fixed order, zeros included.</summary>
    public string FactsDayParts => _snapshot is null ? string.Empty : RecordFactsText.DayParts(_snapshot);

    /// <summary>"Lowest 3.1 · Highest 22.4 · Latest 14.2 on 19 Aug", for the records that
    /// carry numbers.</summary>
    public string FactsValues => _snapshot is null ? string.Empty : RecordFactsText.Values(_snapshot);
    public bool HasFactsValues => FactsValues.Length > 0;

    /// <summary>"308 given · 11 skipped · 6 not recorded" — medication only.</summary>
    public string FactsDoses => _snapshot is null ? string.Empty : RecordFactsText.Doses(_snapshot);
    public bool HasFactsDoses => FactsDoses.Length > 0;

    /// <summary>Nothing in the range. Stated plainly and left alone — an empty stretch is
    /// never framed as a failure to engage (AI/app-voice.md §11).</summary>
    public string FactsNothing => _snapshot is null ? string.Empty : RecordFactsText.Nothing(_snapshot);
    public bool ShowFactsNothing => _snapshot is not null && !_snapshot.HasAny;

    // ── Section 2: the picker ────────────────────────────────────────────────

    /// <summary>The heading the picker sits under, now that it is no longer the whole
    /// sheet.</summary>
    public string PickerHeading => LocalizationManager.Instance.GetString("Today_PickerShowElse");

    /// <summary>Every record a card can hold, in catalog order. Rebuilt per open —
    /// there are seven of them and the selection marks change every time.</summary>
    public ObservableCollection<TodayCardOption> Options { get; } = new();

    public ICommand PickCommand { get; }
    public ICommand DismissCommand { get; }

    /// <summary>Open the picker for one card of one pet.</summary>
    public async Task OpenAsync(Pet? pet, TodayCardSlot slot)
    {
        _pet = pet;
        _slot = slot;

        var config = await _cards.GetConfigAsync(pet);
        var mine = config.For(slot);
        var theirs = config.For(slot == TodayCardSlot.Primary ? TodayCardSlot.Secondary : TodayCardSlot.Primary);

        // Gather first, then clear and refill in one synchronous block — the await
        // above is exactly the window a second open could clear inside (see
        // AI/coding-standards.md, "Rebuilding an ObservableCollection").
        var rows = TodayCardCatalog.Cards
            .Select(meta => new TodayCardOption(meta.Id, ((TodayCardKey)meta.Id) == mine, ((TodayCardKey)meta.Id) == theirs))
            .ToList();

        // The archived-inclusive list, read once and used twice. The picker offers only
        // the LIVE ones — a retired tracker is not something to newly put on Today — but
        // a card already pointing at a retired one must still NAME it: retiring means
        // "stop asking", not "forget" (AI/domain.md).
        var definitions = pet is not null && pet.Id != 0
            ? await _custom.GetAllForPetAsync(pet.Id)
            : new List<CustomTracker>();

        foreach (var c in definitions.Where(c => !c.IsArchived))
        {
            var key = TodayCardKey.Custom(c.Id);
            rows.Add(new TodayCardOption(
                key, key == mine, key == theirs,
                customName: c.Name,
                customIcon: CustomTrackerVisuals.For(c).Icon));
        }

        var name = mine.IsCustom
            ? definitions.FirstOrDefault(c => c.Id == mine.CustomId)?.Name
                ?? LocalizationManager.Instance.GetString("Today_CardCustom")
            : TodayCardCatalog.RecordName(mine.BuiltIn!.Value);

        // The stretch ends TODAY and includes it — the same convention the
        // Constellation's range uses, so the two never disagree by a day.
        var to = DateTime.Now.Date;
        var snapshot = await _facts.GetAsync(pet, mine, to.AddDays(-(FactsDays - 1)), to);

        _recordName = name;
        _snapshot = snapshot;

        Options.Clear();
        foreach (var row in rows)
            Options.Add(row);

        RefreshFacts();
        IsPresented = true;
    }

    /// <summary>Re-raise every displayed string. The getters do the work, so there is
    /// nothing to recompute — same shape as <c>TodayCardItem.RefreshLocalized</c>.</summary>
    private void RefreshFacts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasFacts));
        OnPropertyChanged(nameof(FactsCount));
        OnPropertyChanged(nameof(FactsDayParts));
        OnPropertyChanged(nameof(FactsValues));
        OnPropertyChanged(nameof(HasFactsValues));
        OnPropertyChanged(nameof(FactsDoses));
        OnPropertyChanged(nameof(HasFactsDoses));
        OnPropertyChanged(nameof(FactsNothing));
        OnPropertyChanged(nameof(ShowFactsNothing));
        OnPropertyChanged(nameof(PickerHeading));
    }

    private async Task PickAsync(TodayCardOption? option)
    {
        if (option is null)
            return;

        // Already there: close rather than write the same pair back.
        if (option.IsSelected)
        {
            IsPresented = false;
            return;
        }

        await _cards.SetCardAsync(_pet, _slot, option.Card);
        IsPresented = false;
        Changed?.Invoke();
    }
}
