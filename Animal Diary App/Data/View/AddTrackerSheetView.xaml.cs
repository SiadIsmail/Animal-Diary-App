namespace Animal_Diary_App.Data.View;

/// <summary>The "add a tracker" sheet (log types not yet in the pet's care plan).
/// Purely VM-driven; Manage binds it to the shared ManagePetViewModel.</summary>
public partial class AddTrackerSheetView : ContentView
{
    public AddTrackerSheetView() => InitializeComponent();

    /// <summary>
    /// Give the body its scroll ceiling — see the note in the XAML. This view fills the
    /// page, so its own height is the screen height the sheet sizes itself against.
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (height > 0)
            Body.MaximumHeightRequest = Controls.FelovaBottomSheet.MaxBodyHeight(height);
    }
}
