namespace Animal_Diary_App.Data.View;

/// <summary>The Journal's "+" (add-anything) sheet (see XAML). Chrome from the
/// shared <see cref="Controls.FelovaBottomSheet"/>; bound to the
/// <see cref="ViewModels.JournalLogViewModel"/>.</summary>
public partial class AddAnythingSheetView : ContentView
{
    public AddAnythingSheetView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Give the body its scroll ceiling. This view fills the page, so its own height is
    /// the screen height the sheet sizes itself against. Without the ceiling the list
    /// hugs whatever it contains and a long one is clipped by the sheet's cap rather
    /// than scrolled — the failure only a pet with many custom trackers reaches.
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (height > 0)
            Body.MaximumHeightRequest = Controls.FelovaBottomSheet.MaxBodyHeight(height);
    }
}
