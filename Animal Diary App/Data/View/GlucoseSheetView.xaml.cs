namespace Animal_Diary_App.Data.View;

/// <summary>
/// The Journal's glucose-logging sheet (see the XAML). All chrome + motion come
/// from the shared <see cref="Controls.FelovaBottomSheet"/>; this view only lays out
/// the stepper + toggle and binds them to a <see cref="ViewModels.GlucoseSheetViewModel"/>.
/// </summary>
public partial class GlucoseSheetView : ContentView
{
    public GlucoseSheetView()
    {
        InitializeComponent();
    }
}
