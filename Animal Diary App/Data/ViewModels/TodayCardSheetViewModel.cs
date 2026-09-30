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
    /// showing one record twice: the row says so, so the swap is never a surprise.</summary>
    public bool IsOnOtherCard { get; }

    public string OtherCardNote =>
        IsOnOtherCard ? LocalizationManager.Instance.GetString("Today_PickerOnOtherCard") : string.Empty;
}

/// <summary>
/// One question, one screenful: <i>what matters most today?</i>
///
/// <para><b>This sheet states no facts, and that is deliberate.</b> It used to lead with
/// a 90-day <c>RecordFacts</c> panel above the picker. A fact you must tap a card to
/// discover is a fact most owners never see, and when that panel was specified,
/// Today's "Looking back" section did not exist. Now that it does, the sheet's facts
/// were both redundant and worse-placed, so they are gone and "Looking back" is the ONE
/// place Today states facts about a record. Do not add a second one here.</para>
///
/// <para><see cref="RecordFactsService"/> itself stays exactly where it was: it serves
/// "Looking back" and the appointment summary, and being one service is what makes
/// "computed identically for every record" true by construction rather than by
/// discipline.</para>
///
/// <para>The picker: one screenful of records with no cadence, no toggles and no
/// ordering. It changes <b>only</b> which record the card displays; nothing here starts,
/// stops or configures any tracking (that is the care plan's job on the Manage page), so
/// a card can show a record the pet's plan doesn't ask for and vice versa. The card FACE
/// is untouched too: it states the last recorded value and when, and nothing else,
/// because a count on the face would break the rule that keeps a card from ever reading
/// as a score (AI/domain.md).</para>
///
/// <para>Follows the shared sheet contract in AI/coding-standards.md, minus the
/// <c>Saved</c>/undo pair: a display preference is not a journal entry, and picking
/// again is its own undo.</para>
/// </summary>
public class TodayCardSheetViewModel : BaseViewModel
{
    private readonly TodayCardService _cards;
    private readonly CustomTrackerService _custom;

    private Pet? _pet;
    private TodayCardSlot _slot;

    public TodayCardSheetViewModel(TodayCardService cards, CustomTrackerService custom)
    {
        _cards = cards;
        _custom = custom;

        PickCommand = new Command<TodayCardOption>(async option => await PickAsync(option));
        DismissCommand = new Command(() => IsPresented = false);
    }

    /// <summary>Raised after the pair changed, so Today can re-read both cards.</summary>
    public event Action? Changed;

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    /// <summary>The one question this sheet asks.</summary>
    public string Title => LocalizationManager.Instance.GetString("Today_PickerTitle");

    /// <summary>Empty on purpose: the sheet's subtitle slot is the Caveat handwriting
    /// line, which is a voice, and a picker has nothing to say in one.</summary>
    public string Subtitle => string.Empty;

    /// <summary>Every record a card can hold, in catalog order. Rebuilt per open,
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

        // Gather first, then clear and refill in one synchronous block: the await
        // above is exactly the window a second open could clear inside (see
        // AI/coding-standards.md, "Rebuilding an ObservableCollection").
        var rows = TodayCardCatalog.Cards
            .Select(meta => new TodayCardOption(meta.Id, ((TodayCardKey)meta.Id) == mine, ((TodayCardKey)meta.Id) == theirs))
            .ToList();

        // The archived-inclusive list, read once and used twice. The picker offers only
        // the LIVE ones (a retired tracker is not something to newly put on Today) but
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

        Options.Clear();
        foreach (var row in rows)
            Options.Add(row);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
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
