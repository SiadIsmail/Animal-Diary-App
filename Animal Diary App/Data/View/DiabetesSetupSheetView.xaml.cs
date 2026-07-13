namespace Animal_Diary_App.Data.View;

/// <summary>
/// The reusable diabetes-setup sheet (see the XAML). Chrome + motion come from the
/// shared <see cref="Controls.FelovaBottomSheet"/>; this view lays out the frequency
/// segment + range and binds to a <see cref="ViewModels.DiabetesSetupSheetViewModel"/>.
/// </summary>
public partial class DiabetesSetupSheetView : ContentView
{
    public DiabetesSetupSheetView()
    {
        InitializeComponent();
    }
}
