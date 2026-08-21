namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// One of the two Today stat cards: which record it holds, what that record says, and
/// the tap that changes it.
///
/// <para><b>These two instances are stable</b>: <see cref="MainPageViewModel"/> owns
/// one per slot for the app's lifetime and refreshes them in place with
/// <see cref="Apply"/>. Rebuilding them per load would mean either a leaked
/// <c>LocalizationManager</c> subscription per load or cards that keep the old language
/// after a live switch; here the owning VM re-raises through <see cref="RefreshLocalized"/>
/// instead, the same shape <c>DaySelectionItem</c> uses.</para>
///
/// <para>Every string below is resolved <b>per read</b> from the raw
/// <see cref="TodayCardReading"/> for exactly that reason: nothing localized or
/// culture-formatted is cached in a field.</para>
/// </summary>
public class TodayCardItem : BaseViewModel
{
    private readonly Action<TodayCardItem> _onTap;
    private TodayCardReading _reading;

    public TodayCardItem(TodayCardSlot slot, Action<TodayCardItem> onTap)
    {
        Slot = slot;
        _onTap = onTap;
        _reading = TodayCardReading.Empty(TodayCardId.Weight);

        // Assigned once (see AI/coding-standards.md): an expression-bodied command
        // property hands out a new instance per binding read.
        TapCommand = new Command(() => _onTap(this));
    }

    /// <summary>Which of the two cards this is: the slot the picker writes back to.</summary>
    public TodayCardSlot Slot { get; }

    /// <summary>Which record the card currently holds: shipped or owner-defined.</summary>
    public TodayCardKey Card { get; private set; } = TodayCardId.Weight;

    /// <summary>The whole card is the target: tapping anywhere on it opens the picker.
    /// The pencil in the corner is a cue, never the only way in.</summary>
    public ICommand TapCommand { get; }

    /// <summary>Point the card at a record and give it that record's latest value.</summary>
    public void Apply(TodayCardKey card, TodayCardReading reading)
    {
        Card = card;
        _reading = reading;
        RefreshLocalized();
        OnPropertyChanged(nameof(Card));
    }

    /// <summary>Re-raise every displayed string. Called on a live language switch and
    /// after <see cref="Apply"/>: the getters do the work, so there is nothing to
    /// recompute here.</summary>
    public void RefreshLocalized()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(UnitText));
        OnPropertyChanged(nameof(ValueFontSize));
        OnPropertyChanged(nameof(ShowMood));
        OnPropertyChanged(nameof(MoodEmoji));
        OnPropertyChanged(nameof(MoodColor));
        OnPropertyChanged(nameof(RecordedLabel));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(AccessibilityDescription));
    }

    // ── What the card shows ───────────────────────────────────────────────────

    /// <summary>"Last weigh-in", "Last seizure": every card names a record, never a
    /// running total. The wording rule that keeps a seizure card from ever reading as a
    /// streak lives in the catalog's strings, so no surface can reintroduce one.</summary>
    /// <para>An owner-defined tracker's label is its own NAME, carried on the reading as
    /// verbatim user text, never a localization key. The fallback covers a tracker whose
    /// row has vanished (a hard purge), where there is genuinely nothing left to name.</para>
    public string Label => Card.IsCustom
        ? (_reading.Label.Length > 0 ? _reading.Label : LocalizationManager.Instance.GetString("Today_CardCustom"))
        : TodayCardCatalog.Label(Card.BuiltIn!.Value);

    /// <summary>The same emoji the Journal's chip and timeline tile use for this record
    /// (<see cref="TrackerVisuals"/>), so a customized card is recognizable at a glance.</summary>
    public string Icon => Card.IsCustom
        ? (_reading.Icon.Length > 0 ? _reading.Icon : CustomTrackerVisuals.DefaultIcon)
        : TodayCardCatalog.Visual(Card.BuiltIn!.Value).Icon;

    public bool HasData => _reading.HasData;

    /// <summary>The reading itself. A measured value keeps its exact number; an
    /// observation shows its word (never "3/5"); an event shows when it happened; a
    /// medication shows its own name, verbatim.</summary>
    public string ValueText
    {
        get
        {
            if (!HasData)
                return string.Empty;

            // A custom tracker either recorded a number or only that it happened; the
            // second states WHEN, exactly as the seizure card does.
            if (Card.IsCustom)
                return _reading.Number is decimal own ? own.ToString("0.#") : Stamp();

            return Card.BuiltIn switch
            {
                TodayCardId.Mood => _reading.Mood.GetDisplayName(),
                TodayCardId.Medication => _reading.Text,
                TodayCardId.Seizure => Stamp(),
                // Two stores, one card: whichever was written last, in its own form.
                TodayCardId.Appetite => _reading.Number is decimal grams
                    ? grams.ToString("0.#")
                    : ((AppetiteLevel)_reading.Level).GetDisplayName(),
                TodayCardId.Water => _reading.Number is decimal ml
                    ? ml.ToString("0.#")
                    : ((WaterLevel)_reading.Level).GetDisplayName(),
                // Weight goes through the one weight formatter; everything else left in
                // this arm carries one decimal of its own (Helpers/WeightText.cs).
                TodayCardId.Weight => _reading.Number is decimal kg
                    ? WeightText.Number(kg)
                    : string.Empty,
                _ => _reading.Number?.ToString("0.0") ?? string.Empty,
            };
        }
    }

    /// <summary>The unit beside a measured value (" kg", " ml"), empty for everything
    /// else: including an appetite or water card currently showing an observation.</summary>
    public string UnitText
    {
        get
        {
            if (!HasData || _reading.Number is null)
                return string.Empty;

            // The owner's own unit, in their own words: printed, never looked up.
            if (Card.IsCustom)
                return _reading.Unit.Trim().Length > 0 ? " " + _reading.Unit.Trim() : string.Empty;

            // Weight comes from the one weight formatter rather than a key of its own,
            // the number above already does (Helpers/WeightText.cs).
            if (Card.Is(TodayCardId.Weight))
                return " " + WeightText.Unit.Trim();

            var key = Card.BuiltIn switch
            {
                TodayCardId.Glucose => "Common_MmolSuffix",
                TodayCardId.Water => "Common_MlSuffix",
                TodayCardId.Appetite => "Common_GramSuffix",
                _ => string.Empty,
            };
            if (key.Length == 0)
                return string.Empty;

            // The stored suffixes disagree about leading spaces (" kg" vs "ml"); the
            // card owns the gap so every unit sits the same distance from its number.
            return " " + LocalizationManager.Instance.GetString(key).Trim();
        }
    }

    /// <summary>A number gets the big serif treatment; a word or a name gets the smaller
    /// one so a long mood or medication still fits a half-width card. A presentation
    /// hint held on the VM: the same accepted convention as <c>TimelineItem</c>'s tint.</summary>
    public double ValueFontSize => Card.IsCustom
        ? (_reading.Number is null ? 18 : 27)
        : Card.BuiltIn switch
    {
        TodayCardId.Seizure => 18,
        TodayCardId.Mood or TodayCardId.Medication => 20,
        TodayCardId.Appetite or TodayCardId.Water => _reading.Number is null ? 20 : 27,
        _ => 27,
    };

    /// <summary>The mood card alone carries a colour swatch + face; it ties the card to
    /// the mood ribbon's legend further down the page.</summary>
    public bool ShowMood => HasData && Card.Is(TodayCardId.Mood);

    public string MoodEmoji => ShowMood ? _reading.Mood.GetEmoji() : string.Empty;

    public Color MoodColor => _reading.Mood.GetColor();

    /// <summary>"Recorded today" / "Recorded 3 days ago": WHEN it was written down.
    /// Never a duration since, never a count, never a verdict on the value.</summary>
    public string RecordedLabel => RelativeDay.Recorded(_reading.On);

    /// <summary>The card's "nothing here yet" line, from the existing per-record copy.</summary>
    public string EmptyText => TodayCardCatalog.EmptyText(Card);

    /// <summary>Screen-reader text: the record, its value, and that the card is tappable,
    /// the customization must not be visual-only.</summary>
    public string AccessibilityDescription
    {
        get
        {
            var loc = LocalizationManager.Instance;
            var body = HasData ? $"{ValueText}{UnitText}. {RecordedLabel}" : EmptyText;
            return $"{Label}. {body}. {loc.GetString("Today_CardChangeHint")}";
        }
    }

    /// <summary>"31 Jul · 14:20" for an event card: the moment it happened, stated as a
    /// diary entry. Legacy rows without a time show the date alone.</summary>
    private string Stamp()
    {
        var on = _reading.On!.Value;
        var date = on.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture);
        return _reading.At is TimeSpan at ? $"{date} · {at:hh\\:mm}" : date;
    }
}
