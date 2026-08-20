namespace Animal_Diary_App.Data.View;

/// <summary>The add/edit vet-visit sheet (see XAML). Chrome from the shared
/// <see cref="Controls.FelovaBottomSheet"/>; bound to the
/// <see cref="ViewModels.VetVisitSheetViewModel"/>.</summary>
public partial class VetVisitSheetView : ContentView
{
    public VetVisitSheetView()
    {
        InitializeComponent();
    }

    /// <summary>Give the body its scroll ceiling — five fields plus a note outgrow the
    /// sheet's 90% cap on a short screen, and the cap only clips (see
    /// AddAnythingSheetView for the full note).</summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (height > 0)
            Body.MaximumHeightRequest = Controls.FelovaBottomSheet.MaxBodyHeight(height);
    }
}
