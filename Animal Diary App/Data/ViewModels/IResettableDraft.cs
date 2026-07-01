namespace Animal_Diary_App.Data.ViewModels;

/// <summary>
/// Implemented by ViewModels that hold transient form/draft state. Because the
/// ViewModels are registered as singletons, that draft state outlives any single
/// use of the form; <see cref="ResetDraft"/> gives every form a standard way to
/// return to a blank state. It is called at three lifecycle points:
///   • when an "add" form is opened (so it always starts fresh),
///   • after a successful save or a cancel,
///   • on a global data reset (see <c>MainViewModel.ResetDrafts</c>).
/// </summary>
public interface IResettableDraft
{
    void ResetDraft();
}
