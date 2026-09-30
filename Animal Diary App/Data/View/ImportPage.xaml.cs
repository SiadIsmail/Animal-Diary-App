namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

/// <summary>The AI entry importer (see XAML). Pushed from the developer sheet's import
/// section; constructed with the shared <see cref="MainViewModel"/> like every other
/// pushed page.</summary>
public partial class ImportPage : ContentPage
{
    private readonly MainViewModel vm;

    public ImportPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // A fresh screen every time. The VM is a singleton, so a previous session's
        // preview would otherwise still be sitting there offering an Import button for a
        // file the owner already imported.
        vm.ImportVM.ResetDraft();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try
        {
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Import] back failed: {ex}");
        }
    }
}
