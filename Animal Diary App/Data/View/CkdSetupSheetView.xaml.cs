namespace Animal_Diary_App.Data.View;

/// <summary>
/// The reusable kidney-care setup sheet (see the XAML). Chrome + motion come from the
/// shared <see cref="Controls.FelovaBottomSheet"/>; this view lays out the weigh-in
/// cadence + toggles and binds to a <see cref="ViewModels.CkdSetupSheetViewModel"/>.
/// </summary>
public partial class CkdSetupSheetView : ContentView
{
    public CkdSetupSheetView()
    {
        InitializeComponent();
    }
}
