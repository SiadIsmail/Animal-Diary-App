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
/// The "what matters most" sheet: tap a Today card, choose what it shows.
///
/// <para>Deliberately one screenful of records with no cadence, no toggles and no
/// ordering — this is a question about the owner's day, not a dashboard editor. It
/// changes <b>only</b> which record the card displays; nothing here starts, stops or
/// configures any tracking (that is the care plan's job on the Manage page), so a card
/// can show a record the pet's plan doesn't ask for and vice versa.</para>
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

    public string Title => LocalizationManager.Instance.GetString("Today_PickerTitle");

    public string Subtitle => LocalizationManager.Instance.Format("Today_PickerSub", _pet?.Name ?? string.Empty);

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

        // The owner's own trackers, after the shipped records. The LIVE list, not the
        // archived-inclusive one: a retired tracker is not something to newly put on
        // Today, even though a card already pointing at one keeps working.
        if (pet is not null && pet.Id != 0)
        {
            foreach (var c in await _custom.GetForPetAsync(pet.Id))
            {
                var key = TodayCardKey.Custom(c.Id);
                rows.Add(new TodayCardOption(
                    key, key == mine, key == theirs,
                    customName: c.Name,
                    customIcon: CustomTrackerVisuals.For(c).Icon));
            }
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
