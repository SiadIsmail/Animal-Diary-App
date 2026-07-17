namespace Animal_Diary_App.Data.View.Controls;

using Animal_Diary_App.Helpers;

/// <summary>
/// Code-behind for the shared confirmation/undo pill (behaviour ported verbatim
/// from CalendarPage's original private toast). One toast at a time: a newer
/// <see cref="Show"/> supersedes the pending auto-hide via a sequence counter.
///
/// Semantics: <c>undoAsync</c> runs only when the user taps Undo;
/// <c>expiredAsync</c> runs only when the toast times out WITHOUT an undo — the
/// hook deferred deletions use to commit. A superseded toast calls neither
/// (the caller staged the new state itself; callbacks must stay idempotent).
/// </summary>
public partial class UndoToast : ContentView
{
    private int _seq;
    private Func<Task>? _pendingUndo;

    public UndoToast()
    {
        InitializeComponent();
    }

    /// <summary>Transient confirmation with no undo (2.4s).</summary>
    public void Show(string message) => Show(message, null, null);

    /// <summary>Confirmation with an Undo button (6s, so it stays reachable).</summary>
    public async void Show(string message, Func<Task>? undoAsync, Func<Task>? expiredAsync = null)
    {
        try
        {
            ToastLabel.Text = message;
            _pendingUndo = undoAsync;
            UndoButton.IsVisible = undoAsync != null;
            // Only capture taps while an Undo button is present; otherwise stay
            // input-transparent so the transient toast never blocks the page.
            InputTransparent = undoAsync == null;

            int seq = ++_seq;
            int ms = undoAsync != null ? 6000 : 2400;

            if (ReducedMotion.IsEnabled)
            {
                ToastBorder.TranslationY = 0;
                ToastBorder.Opacity = 1;
                await Task.Delay(ms);
                if (seq != _seq)
                    return;
                HideNow();
                await RunAsync(expiredAsync);
                return;
            }

            ToastBorder.TranslationY = 16;
            ToastBorder.Opacity = 0;
            await Task.WhenAll(
                ToastBorder.FadeTo(1, 220, Easing.CubicOut),
                ToastBorder.TranslateTo(0, 0, 220, Easing.CubicOut));

            await Task.Delay(ms);
            if (seq != _seq)
                return;
            await ToastBorder.FadeTo(0, 260, Easing.CubicIn);
            HideNow();
            await RunAsync(expiredAsync);
        }
        catch (Exception ex)
        {
            // async void entry point — an escaping exception would kill the process.
            System.Diagnostics.Debug.WriteLine($"[UndoToast] show failed: {ex}");
        }
    }

    private async void OnUndoTapped(object? sender, TappedEventArgs e)
    {
        var undo = _pendingUndo;
        _seq++; // cancel the pending auto-hide (and its expired callback)
        HideNow();
        await RunAsync(undo);
    }

    private void HideNow()
    {
        ToastBorder.Opacity = 0;
        InputTransparent = true;
        UndoButton.IsVisible = false;
        _pendingUndo = null;
    }

    private static async Task RunAsync(Func<Task>? action)
    {
        try
        {
            if (action != null)
                await action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UndoToast] callback failed: {ex}");
        }
    }
}
