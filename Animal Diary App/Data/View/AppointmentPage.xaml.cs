namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;

/// <summary>
/// The appointment page (see XAML). Pushed from the Care page's "Vet visits" row and
/// from the band on Today, never a Shell tab: a tab is for something used daily, and a
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
        vm.ExportSheetVM.AddVisitRequested += OnAddVisitRequested;
        vm.VetVisitSheetVM.Saved += OnVisitSaved;
        vm.VetVisitSheetVM.Deleted += OnVisitDeleted;
        vm.VetQuestionSheetVM.Saved += OnQuestionSaved;

        try
        {
            await vm.AppointmentVM.LoadAsync();
        }
        catch (Exception ex)
        {
            // A failed load degrades to the empty state, never crash the page.
            System.Diagnostics.Debug.WriteLine($"[Appointment] load failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        vm.AppointmentVM.FullSummaryRequested -= OnFullSummaryRequested;
        vm.ExportSheetVM.ViewRequested -= OnReportViewRequested;
        vm.ExportSheetVM.AddVisitRequested -= OnAddVisitRequested;
        vm.VetVisitSheetVM.Saved -= OnVisitSaved;
        vm.VetVisitSheetVM.Deleted -= OnVisitDeleted;
        vm.VetQuestionSheetVM.Saved -= OnQuestionSaved;
    }

    /// <summary>Android back closes an open sheet before it navigates
    /// (<c>Controls/BackDismiss</c>): the page hosts two.</summary>
    protected override bool OnBackButtonPressed() =>
        Controls.BackDismiss.TryCloseTopmostOverlay(this) || base.OnBackButtonPressed();

    // A visit was written → reload and confirm, with NO Undo button. Editing a visit is
    // reversed by opening the sheet again, and UndoToast shows its button whenever a
    // callback is present, so handing it a no-op meant a button that visibly did
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

    /// <summary>The export sheet's "View" button: push the in-app preview for the
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

    /// <summary>The export sheet's one-shot "was that for a visit?" offer. Straight into
    /// the visit sheet this page already hosts.</summary>
    private void OnAddVisitRequested()
    {
        try
        {
            vm.AppointmentVM.AddVisitCommand.Execute(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Appointment] add-visit open failed: {ex}");
        }
    }

    /// <summary>
    /// The owner reached the bottom of the page. That is what "used their free summary"
    /// means when they did not export it: they read it to the end.
    ///
    /// <para><b>Why the bottom of the page and not the top of the summary.</b> The flag
    /// spends someone's one free artifact, so the bar has to be genuine use rather than a
    /// glance. Opening the page is not it: the brief for this feature is explicit that
    /// someone who taps in, looks confused and leaves has not had their free one, and an
    /// on-appearing flag would take it from them anyway. Scrolling past the ledger, the
    /// new records and the whole "what you wrote down" block is the cheapest honest
    /// signal available without instrumenting reading itself.</para>
    ///
    /// <para>The VM is idempotent and re-checks its own preconditions, so this firing on
    /// every scroll tick, on a page with no summary, or on the paid tier is a no-op.</para>
    /// </summary>
    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        // Fully qualified: this file lives in Animal_Diary_App.Data.View, so a bare "View"
        // binds to the namespace.
        if (PageScroll.Content is not Microsoft.Maui.Controls.View content)
            return;

        // Within a line or two of the end. An exact equality never fires: the platforms
        // disagree about sub-pixel content height, and Android's overscroll bounce lands
        // ScrollY fractionally short.
        const double slack = 24;
        var reachedEnd = e.ScrollY + PageScroll.Height >= content.Height - slack;
        if (!reachedEnd)
            return;

        MarkSummaryUsedAsync().Forget();
    }

    private async Task MarkSummaryUsedAsync()
    {
        try
        {
            await vm.AppointmentVM.MarkSummaryUsedAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Appointment] mark-used failed: {ex}");
        }
    }

    // "Full summary" routes to the EXISTING export sheet, pre-filled with the
    // since-last-visit stretch. There is deliberately no second PDF path, and no pop
    // either: this page is reached from BOTH Care and Today, so it cannot assume what a
    // pop lands on. The sheet is hosted here instead and opens in place.
    private async void OnFullSummaryRequested(int days)
    {
        try
        {
            // Exporting it is the other way to have genuinely used it, and the stronger
            // one: they took it out of the app to hand over. Marked here rather than in
            // the export sheet because the sheet is shared with the Pets page, where an
            // export has nothing to do with an appointment summary.
            await MarkSummaryUsedAsync();
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
