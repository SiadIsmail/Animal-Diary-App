namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;

/// <summary>
/// The appointment page (see XAML). Pushed from the Care page's "Vet visits" row and
/// from the band on Today — never a Shell tab: a tab is for something used daily, and a
/// vet visit happens two to four times a year.
/// </summary>
public partial class AppointmentPage : ContentPage
{
    private readonly MainViewModel vm;

    public AppointmentPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        vm.AppointmentVM.FullSummaryRequested += OnFullSummaryRequested;
        vm.VetVisitSheetVM.Saved += OnChanged;
        vm.VetQuestionSheetVM.Saved += OnChanged;

        try
        {
            await vm.AppointmentVM.LoadAsync();
        }
        catch (Exception ex)
        {
            // A failed load degrades to the empty state — never crash the page.
            System.Diagnostics.Debug.WriteLine($"[Appointment] load failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        vm.AppointmentVM.FullSummaryRequested -= OnFullSummaryRequested;
        vm.VetVisitSheetVM.Saved -= OnChanged;
        vm.VetQuestionSheetVM.Saved -= OnChanged;
    }

    /// <summary>Android back closes an open sheet before it navigates
    /// (<c>Controls/BackDismiss</c>) — the page hosts two.</summary>
    protected override bool OnBackButtonPressed() =>
        Controls.BackDismiss.TryCloseTopmostOverlay(this) || base.OnBackButtonPressed();

    // A visit or a question was written or removed → reload, then the toast. The sheet
    // has already closed itself by the time this runs, which is what keeps the toast
    // visible: it is declared below the sheet hosts, so one raised over an open sheet
    // would sit behind the scrim.
    private async void OnChanged(JournalSaveResult result)
    {
        try
        {
            await vm.AppointmentVM.LoadAsync();
            Toast.Show(result.Message, async () =>
            {
                await result.UndoAsync();
                await vm.AppointmentVM.LoadAsync();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Appointment] refresh failed: {ex}");
        }
    }

    // "Full summary" routes to the EXISTING export sheet, pre-filled with the
    // since-last-visit stretch. There is deliberately no second PDF path — the export
    // sheet lives on the Care page, so this pops back to it and opens it there.
    private async void OnFullSummaryRequested(int days)
    {
        try
        {
            await Navigation.PopAsync();
            await vm.ExportSheetVM.OpenForRangeAsync(days);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Appointment] export handoff failed: {ex}");
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();
}
