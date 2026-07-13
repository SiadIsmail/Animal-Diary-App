namespace Animal_Diary_App.Data.View;

/// <summary>
/// The reusable epilepsy setup sheet (see the XAML). Chrome + motion come from the
/// shared <see cref="Controls.FelovaBottomSheet"/>; this view shows one explanation +
/// action, bound to an <see cref="ViewModels.EpilepsySetupSheetViewModel"/>.
/// </summary>
public partial class EpilepsySetupSheetView : ContentView
{
    public EpilepsySetupSheetView()
    {
        InitializeComponent();
    }
}
