namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;

public partial class DocumentsPage : ContentPage
{
    private readonly MainViewModel vm;

    public DocumentsPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        vm.DocumentsVM.OpenRequested += OnOpenRequested;
        vm.DocumentsVM.DeleteStaged += OnDeleteStaged;

        try
        {
            await vm.DocumentsVM.LoadAsync();
        }
        catch (Exception ex)
        {
            // A failed load degrades to the empty state — never crash the page.
            System.Diagnostics.Debug.WriteLine($"[Documents] load failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        vm.DocumentsVM.OpenRequested -= OnOpenRequested;
        vm.DocumentsVM.DeleteStaged -= OnDeleteStaged;

        // Leaving the page commits a staged delete — the toast's Undo must not
        // outlive the list it would restore into.
        vm.DocumentsVM.CommitPendingDeleteAsync().Forget();
    }

    private async void OnOpenRequested(VetReportFile report)
    {
        try
        {
            vm.ReportPreviewVM.Open(report);
            await Navigation.PushAsync(new ReportPreviewPage(vm));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Documents] preview push failed: {ex}");
        }
    }

    // The deferred-delete contract: Undo restores the row; expiry commits it.
    private void OnDeleteStaged(string message) =>
        Toast.Show(message, vm.DocumentsVM.UndoDeleteAsync, vm.DocumentsVM.CommitPendingDeleteAsync);

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();
}
