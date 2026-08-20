namespace Animal_Diary_App.Data.View;

/// <summary>The "question for the vet" sheet (see XAML). Chrome from the shared
/// <see cref="Controls.FelovaBottomSheet"/>; bound to the
/// <see cref="ViewModels.VetQuestionSheetViewModel"/>.</summary>
public partial class VetQuestionSheetView : ContentView
{
    public VetQuestionSheetView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Give the body its scroll ceiling — same reason as the "+" sheet. This view fills
    /// the page, so its own height is what the sheet sizes itself against; without the
    /// ceiling a long list of open questions is clipped by the sheet's 90% cap rather
    /// than scrolled inside it, which puts the input field off-screen.
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (height > 0)
            Body.MaximumHeightRequest = Controls.FelovaBottomSheet.MaxBodyHeight(height);
    }
}
