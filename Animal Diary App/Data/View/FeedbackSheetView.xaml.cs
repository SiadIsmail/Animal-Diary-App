namespace Animal_Diary_App.Data.View;

/// <summary>The "feedback or a problem" sheet (Discord / direct email). Purely
/// VM-driven; the host page binds it to the shared FeedbackSheetViewModel.</summary>
public partial class FeedbackSheetView : ContentView
{
    public FeedbackSheetView() => InitializeComponent();
}
