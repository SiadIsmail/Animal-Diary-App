namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

public partial class ReportPreviewPage : ContentPage
{
    public ReportPreviewPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        BindingContext = mainViewModel;
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();
}
