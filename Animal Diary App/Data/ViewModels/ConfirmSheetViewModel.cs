namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Helpers;

/// <summary>One choice on the confirm sheet. <see cref="Id"/> is what
/// <see cref="ConfirmSheetViewModel.AskAsync"/> hands back, so callers compare against
/// their own constant rather than against a localized label.</summary>
public sealed class ConfirmOption
{
    public required string Id { get; init; }
    public required string Label { get; init; }

    /// <summary>Optional second line — what this choice actually does. Empty hides it.</summary>
    public string Description { get; init; } = string.Empty;
    public bool HasDescription => !string.IsNullOrEmpty(Description);

    /// <summary>Renders in the rose accent. The consequence, not the escape.</summary>
    public bool IsDestructive { get; init; }
}

/// <summary>
/// The app's own multi-choice confirmation, on the shared FelovaBottomSheet.
///
/// Replaces <c>DisplayActionSheet</c> for anything with more than two outcomes.
/// Android renders that API's <c>cancel</c> and <c>destruction</c> as real dialog
/// buttons and everything else as list rows that read like body text — and in every
/// one of this app's uses the "everything else" slot held the GENTLEST option
/// ("Save a copy first", "Device only"). So the safe path looked like prose while
/// destruction looked like the button, which is precisely backwards for a flow
/// someone reaches while upset. Offering the export before a deletion is required
/// (app-voice §13); offering it invisibly doesn't count.
///
/// Two-outcome confirmations stay on the native <c>DisplayAlert</c> — those render
/// both choices as buttons and are not ambiguous. This is for real decisions, which
/// are input, and input belongs in a sheet (coding-standards.md).
/// </summary>
public sealed class ConfirmSheetViewModel : BaseViewModel
{
    private TaskCompletionSource<string?>? _pending;

    public ConfirmSheetViewModel()
    {
        PickCommand = new Command<ConfirmOption>(Pick);
        DismissCommand = new Command(() => IsPresented = false);
    }

    private bool _isPresented;
    public bool IsPresented
    {
        get => _isPresented;
        set
        {
            // Closing by ANY route resolves the caller: scrim tap, Android back, or a
            // page tearing the sheet down. Without this an awaiting caller would hang
            // on a sheet the user had already dismissed.
            if (SetProperty(ref _isPresented, value) && !value)
                Complete(null);
        }
    }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;
    public bool HasBody => !string.IsNullOrEmpty(Body);

    public string CancelLabel { get; private set; } = string.Empty;

    public IReadOnlyList<ConfirmOption> Options { get; private set; } = Array.Empty<ConfirmOption>();

    public ICommand PickCommand { get; }
    public ICommand DismissCommand { get; }

    /// <summary>
    /// Present the choice and wait for it. Returns the chosen <see cref="ConfirmOption.Id"/>,
    /// or null if the owner backed out — cancelling is always a valid answer and never
    /// needs its own option in the list.
    /// </summary>
    public Task<string?> AskAsync(
        string title,
        string? body,
        IReadOnlyList<ConfirmOption> options,
        string? cancelLabel = null)
    {
        // A second ask while one is still open resolves the first as cancelled rather
        // than abandoning its caller mid-await.
        Complete(null);

        Title = title;
        Body = body ?? string.Empty;
        Options = options;
        CancelLabel = string.IsNullOrEmpty(cancelLabel)
            ? LocalizationManager.Instance.GetString("Common_Cancel")
            : cancelLabel;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(HasBody));
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(CancelLabel));

        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        IsPresented = true;
        return pending.Task;
    }

    private void Pick(ConfirmOption? option)
    {
        // Resolve BEFORE closing: the IsPresented setter completes any still-pending
        // ask as cancelled, so closing first would throw the answer away.
        Complete(option?.Id);
        IsPresented = false;
    }

    private void Complete(string? id)
    {
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(id);
    }
}
