namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>One question already on the list, shown back to the owner as they add
/// another. Read-only here: it exists so writing a second question does not feel like
/// writing into the dark. <see cref="Text"/> is their own words and is never
/// translated.</summary>
public sealed record VetQuestionLine(string Text);

/// <summary>
/// "Question for the vet" — the one thing an owner reliably forgets, written down
/// where they already are.
///
/// <para><b>It is not a journal entry, and the sheet is built so it cannot become
/// one.</b> Nothing here touches a tracker, a care plan or <c>PendingEngine</c>; it
/// never produces a "still to do" chip and it never reaches the timeline. It is a note
/// to self about a conversation, not a record of the animal — so there is no date, no
/// time picker and no value. The date it was thought of is stamped by the service and
/// never asked for.</para>
///
/// <para>Follows the shared sheet contract in AI/coding-standards.md: it raises
/// <c>Saved</c> with a <see cref="JournalSaveResult"/> — the warm line plus the undo —
/// exactly like the input sheets, so the page's toast works unchanged. The page shows
/// it through the quiet handler rather than the celebratory one: bubbles rising off a
/// question would be the app congratulating someone for worrying.</para>
///
/// <para>Ticking a question answered and deleting one live on the appointment page,
/// which is where the list is read. This sheet only ever adds.</para>
/// </summary>
public class VetQuestionSheetViewModel : BaseViewModel
{
    private readonly VetQuestionService _questions;

    private int _petId;
    private string _petName = string.Empty;

    public VetQuestionSheetViewModel(VetQuestionService questions)
    {
        _questions = questions;

        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    public event Action<JournalSaveResult>? Saved;

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => LocalizationManager.Instance.GetString("Vet_QuestionTitle");

    public string Subtitle => LocalizationManager.Instance.Format("Vet_QuestionSub", _petName);

    private string _questionText = string.Empty;
    public string QuestionText
    {
        get => _questionText;
        set
        {
            if (SetProperty(ref _questionText, value))
                OnPropertyChanged(nameof(CanSave));
        }
    }

    /// <summary>An empty question is not a question. No error label for it — the Save
    /// button simply does nothing, the same as every other sheet here.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(QuestionText);

    /// <summary>What is already on the list for this pet, oldest first. Read-only and
    /// shown above the field so the owner can see they are adding to something.</summary>
    public ObservableCollection<VetQuestionLine> Open { get; } = new();

    public bool HasOpen => Open.Count > 0;

    public string OpenHeading => LocalizationManager.Instance.GetString("Vet_QuestionOpenHeading");

    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    /// <summary>Open the sheet for one pet. Takes the same
    /// <c>(petId, petName, date)</c> shape as every Journal sheet so the page's one
    /// funnel can route to it — the date is deliberately ignored, because a question is
    /// not about the day the owner happens to be looking at.</summary>
    public async Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        QuestionText = string.Empty;

        // Gathered before the collection is touched — the await is exactly the window a
        // second open could clear inside (AI/coding-standards.md).
        var rows = petId == 0
            ? new List<VetQuestion>()
            : await _questions.GetOpenAsync(petId);

        Open.Clear();
        foreach (var row in rows)
            Open.Add(new VetQuestionLine(row.Text));

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(OpenHeading));
        OnPropertyChanged(nameof(HasOpen));
        IsPresented = true;
    }

    private async Task SaveAsync()
    {
        if (!CanSave || _petId == 0)
            return;

        var id = await _questions.AddAsync(_petId, QuestionText);

        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.GetString("Vet_QuestionToast"),
            () => _questions.DeleteAsync(id)));
    }
}
