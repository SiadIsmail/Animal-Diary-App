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
        vm.ExportSheetVM.ViewRequested += OnReportViewRequested;
        vm.VetVisitSheetVM.Saved += OnVisitSaved;
        vm.VetVisitSheetVM.Deleted += OnVisitDeleted;
        vm.VetQuestionSheetVM.Saved += OnQuestionSaved;

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
        vm.ExportSheetVM.ViewRequested -= OnReportViewRequested;
        vm.VetVisitSheetVM.Saved -= OnVisitSaved;
        vm.VetVisitSheetVM.Deleted -= OnVisitDeleted;
        vm.VetQuestionSheetVM.Saved -= OnQuestionSaved;
    }

    /// <summary>Android back closes an open sheet before it navigates
    /// (<c>Controls/BackDismiss</c>) — the page hosts two.</summary>
    protected override bool OnBackButtonPressed() =>
        Controls.BackDismiss.TryCloseTopmostOverlay(this) || base.OnBackButtonPressed();

    // A visit was written → reload and confirm, with NO Undo button. Editing a visit is
    // reversed by opening the sheet again, and UndoToast shows its button whenever a
    // callback is present — so handing it a no-op meant a button that visibly did
    // nothing. Show(message) is the shape for a confirmation with nothing to take back.
    private async void OnVisitSaved(string message) => await RefreshAsync(message, undo: null);

    // A visit or a question was removed / added → reload, then a toast that can undo it.
    // The sheet has already closed itself by the time this runs, which is what keeps the
    // toast visible: it is declared below the sheet hosts, so one raised over an open
    // sheet would sit behind the scrim.
    private async void OnVisitDeleted(JournalSaveResult result) =>
        await RefreshAsync(result.Message, result.UndoAsync);

    private async void OnQuestionSaved(JournalSaveResult result) =>
        await RefreshAsync(result.Message, result.UndoAsync);

    private async Task RefreshAsync(string message, Func<Task>? undo)
    {
        try
        {
            await vm.AppointmentVM.LoadAsync();

            if (undo is null)
                Toast.Show(message);
            else
                Toast.Show(message, async () =>
                {
                    await undo();
                    await vm.AppointmentVM.LoadAsync();
                });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Appointment] refresh failed: {ex}");
        }
    }

    /// <summary>The export sheet's "View" button — push the in-app preview for the
    /// report it just generated (navigation belongs to pages, not VMs). Mirrors
    /// PetsPage, which hosts the same sheet.</summary>
    private async void OnReportViewRequested(Data.Models.VetReportFile report)
    {
        try
        {
            vm.ReportPreviewVM.Open(report);
            await Navigation.PushAsync(new ReportPreviewPage(vm));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Appointment] preview push failed: {ex}");
        }
    }

    // "Full summary" routes to the EXISTING export sheet, pre-filled with the
    // since-last-visit stretch. There is deliberately no second PDF path — and no pop
    // either: this page is reached from BOTH Care and Today, so it cannot assume what a
    // pop lands on. The sheet is hosted here instead and opens in place.
    private async void OnFullSummaryRequested(int days)
    {
        try
        {
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
