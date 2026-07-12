namespace Animal_Diary_App.Data.ViewModels;

using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Helpers;

/// <summary>
/// Backs the Journal's weight sheet — a value stepper (ported from the old inline
/// weigh-in editor) in the shared <see cref="Controls.FelovaBottomSheet"/>. Saves to
/// the day's <see cref="PetEntry"/>; Save's undo restores the previous weight.
/// The kilogram value is a precise readout — shown, never rounded away.
/// </summary>
public class WeightSheetViewModel : BaseViewModel
{
    private readonly PetEntryService _petEntries;

    private int _petId;
    private string _petName = string.Empty;
    private DateTime _date;

    public WeightSheetViewModel(PetEntryService petEntries)
    {
        _petEntries = petEntries;

        StepCommand = new Command<string>(OnStep);
        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    public event Action<JournalSaveResult>? Saved;

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => LocalizationManager.Instance.Format("Journal_WeightTitle", _petName);
    public string Subtitle => LocalizationManager.Instance.Format("Journal_WeightSub", _date);

    private string _valueText = string.Empty;
    public string ValueText { get => _valueText; set => SetProperty(ref _valueText, value); }

    public ICommand StepCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    public async Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        _date = date.Date;

        var existing = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, petId);
        decimal start;
        if (existing != null && existing.Weight > 0)
            start = existing.Weight;
        else
        {
            var latest = await _petEntries.GetLatestWeightEntryAsync(petId);
            start = latest?.Weight ?? 4.0m;
        }
        ValueText = start.ToString("0.0", CultureInfo.CurrentCulture);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
    }

    private void OnStep(string? raw)
    {
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var delta))
            return;

        var current = InputParser.TryParsePositive(ValueText, out var v) ? (double)v : 0;
        var next = Math.Max(0, Math.Round(current + delta, 1));
        ValueText = next.ToString("0.0", CultureInfo.CurrentCulture);
    }

    private async Task SaveAsync()
    {
        if (!InputParser.TryParsePositive(ValueText, out var weight) || weight <= 0)
            return;

        var entry = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, _petId);
        var prevWeight = entry?.Weight ?? 0m;

        await WriteWeightAsync(weight);

        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.GetString("Toast_WeightSaved1"),
            () => WriteWeightAsync(prevWeight)));
    }

    // Write just the weight column of the day's entry, leaving mood untouched.
    private async Task WriteWeightAsync(decimal weight)
    {
        var entry = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, _petId);
        if (entry != null)
        {
            entry.Weight = weight;
            await _petEntries.UpdatePetEntryAsync(entry);
        }
        else
        {
            await _petEntries.SavePetEntryAsync(new PetEntry
            {
                PetId = _petId,
                Date = _date,
                Weight = weight
            });
        }
    }
}
