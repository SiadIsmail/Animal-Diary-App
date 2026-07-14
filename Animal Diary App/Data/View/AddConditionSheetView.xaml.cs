namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

/// <summary>
/// Manage Pet's "add a condition" sheet (see the XAML). Binds to the
/// <see cref="ViewModels.ManagePetViewModel"/> supplied by the page.
/// </summary>
public partial class AddConditionSheetView : ContentView
{
    public AddConditionSheetView()
    {
        InitializeComponent();
    }

    // Diagnostic: force the BindableLayout to drop and regenerate its children,
    // to test whether Android MAUI fails to regen items on an already-created
    // hidden view when ItemsSource changes.
    public void RefreshItems()
    {
        if (BindingContext is ManagePetViewModel vm)
        {
            var items = vm.AddConditionOptions;

            BindableLayout.SetItemsSource(ConditionList, null);
            BindableLayout.SetItemsSource(ConditionList, items);
        }
    }
}
