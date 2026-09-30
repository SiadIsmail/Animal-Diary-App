namespace Animal_Diary_App.Data.View;

/// <summary>Settings → Cloud Features sheet (see XAML). Chrome from the shared
/// <see cref="Controls.FelovaBottomSheet"/>; bound to a
/// <see cref="ViewModels.CloudSheetViewModel"/>.</summary>
public partial class CloudSheetView : ContentView
{
    public CloudSheetView()
    {
        InitializeComponent();
    }

    /// <summary>Give the body its scroll ceiling. The sheet grows with its content and is
    /// capped at 90% of the screen, and the cap only CLIPS: a body that can outgrow it has
    /// to own its own scroller and its own ceiling. See FelovaBottomSheet's header, and
    /// VetVisitSheetView for the same pair.
    ///
    /// <para>Both halves are load-bearing. <c>VerticalOptions="Start"</c> alone overflows
    /// the cap; the ceiling alone stretches a short body to fill it.</para></summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (height > 0)
            Body.MaximumHeightRequest = Controls.FelovaBottomSheet.MaxBodyHeight(height);
    }
}
