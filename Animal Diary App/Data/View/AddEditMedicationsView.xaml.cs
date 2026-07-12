namespace Animal_Diary_App.Data.View;

/// <summary>
/// Add / edit a medication. The slide-up chrome and motion now live in the shared
/// <see cref="Controls.FelovaBottomSheet"/>; this view just wires the medication
/// form into it. Presentation is driven by
/// <see cref="ViewModels.MedicationViewModel.IsAddEditSheetVisible"/>, bound to the
/// sheet's <c>IsPresented</c> in XAML — no per-view animation code needed.
/// </summary>
public partial class AddEditMedicationsView : ContentView
{
    public AddEditMedicationsView()
    {
        InitializeComponent();
    }
}
