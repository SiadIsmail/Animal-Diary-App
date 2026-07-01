using System.ComponentModel;
using Animal_Diary_App.Data.ViewModels;

namespace Animal_Diary_App.Data.View;

/// <summary>
/// Slide-up bottom sheet for adding/editing a medication. Embedded as an overlay
/// in <see cref="MedicationsPage"/>. Visibility is driven by
/// <see cref="MedicationViewModel.IsAddEditSheetVisible"/>; this code-behind
/// plays the slide + fade animation when that flag flips so the sheet animates
/// in and out rather than popping.
/// </summary>
public partial class AddEditMedicationsView : ContentView
{
    private MedicationViewModel? medicationVM;
    private bool isAnimating;

    public AddEditMedicationsView()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        // Cap the sheet to most of the screen so a tall form (many reminder
        // times) scrolls its body instead of pushing the footer off-screen.
        if (height > 0)
            SheetContainer.MaximumHeightRequest = height * 0.9;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (medicationVM != null)
            medicationVM.PropertyChanged -= OnMedicationVMPropertyChanged;

        medicationVM = (BindingContext as MainViewModel)?.MedicationVM;

        if (medicationVM != null)
            medicationVM.PropertyChanged += OnMedicationVMPropertyChanged;
    }

    private async void OnMedicationVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MedicationViewModel.IsAddEditSheetVisible) || medicationVM == null)
            return;

        if (medicationVM.IsAddEditSheetVisible)
            await ShowAsync();
        else
            await HideAsync();
    }

    private async Task ShowAsync()
    {
        if (isAnimating)
            return;
        isAnimating = true;

        // Start fully below the screen and transparent, then slide up + fade in.
        var offset = SheetContainer.Height > 0 ? SheetContainer.Height : 600;
        SheetContainer.TranslationY = offset;
        Scrim.Opacity = 0;
        IsVisible = true;

        await Task.WhenAll(
            Scrim.FadeTo(1, 250, Easing.CubicOut),
            SheetContainer.TranslateTo(0, 0, 250, Easing.CubicOut));

        isAnimating = false;
    }

    private async Task HideAsync()
    {
        if (isAnimating)
            return;
        isAnimating = true;

        var offset = SheetContainer.Height > 0 ? SheetContainer.Height : 600;

        await Task.WhenAll(
            Scrim.FadeTo(0, 200, Easing.CubicIn),
            SheetContainer.TranslateTo(0, offset, 200, Easing.CubicIn));

        IsVisible = false;
        SheetContainer.TranslationY = 0;

        isAnimating = false;
    }
}
