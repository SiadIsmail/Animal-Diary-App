namespace Animal_Diary_App.Data.View;

/// <summary>The shared multi-choice confirmation sheet. Purely VM-driven; each hosting
/// page binds it to the shared ConfirmSheetViewModel.</summary>
public partial class ConfirmSheetView : ContentView
{
    public ConfirmSheetView() => InitializeComponent();
}
